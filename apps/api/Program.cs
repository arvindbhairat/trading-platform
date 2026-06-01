using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using SignalStack.Api.Admin;
using SignalStack.Api.Audit;
using SignalStack.Storage.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Fyers;
using SignalStack.Api.Observability;
using SignalStack.Api.PhaseEnforcement;
using SignalStack.Api.Pld;
using SignalStack.Storage.Fyers;
using SignalStack.Api.PrivacyRequest;
using SignalStack.Api.DataBreach;
using SignalStack.Api.Sessions;
using SignalStack.Api.Universe;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using SignalStack.Api.Historical;
using SignalStack.Api.Backtesting;
using SignalStack.Api.Signals;
using SignalStack.Api.Notifications;
using SignalStack.Api.Push;
using SignalStack.Api.TelegramBot;
using SignalStack.Api.LedgerWriters;
using SignalStack.Api.Portfolio;
using SignalStack.Api.Risk;
using SignalStack.Api.Execution;
using SignalStack.Storage.Admin;
using SignalStack.Storage.SysConfig;
using SignalStack.Storage.LedgerWriters;
using SignalStack.Storage.Notifications;
using SignalStack.Storage.Signals;
using SignalStack.Storage.Universe;
using SignalStack.Notifications.TelegramBot;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Configuration.Ledger;
using SignalStack.Migrations;

const string ApiServiceName = "SignalStack.Api";
const string AuthRateLimitPolicy = "auth-fixed-window";

// Load local .env file from repo root for development connection strings.
DotNetEnv.Env.Load("../../.env");

var builder = WebApplication.CreateBuilder(args);

builder.AddSignalStackBootstrapConfiguration(ApiServiceName);
builder.AddSignalStackTelemetry(ApiServiceName);

// ── API Contract Standards ──────────────────────────────────────────────
// Phase 1: OpenAPI generation scaffolding — exposes /openapi/v1.json
builder.Services.AddOpenApi();

// Phase 2: Global snake_case naming policy for all HTTP JSON.
// Fixes compound snake_case fields (signal_type_id, rme_configuration, etc.)
// binding to PascalCase C# DTO properties.
// See docs/api-contract-standardization-plan.md for full rationale.
builder.Services.ConfigureHttpJsonOptions(options =>
{
  options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
  options.SerializerOptions.PropertyNameCaseInsensitive = true;
});
// ────────────────────────────────────────────────────────────────────────

var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDb");
var mongoDatabaseName = builder.Configuration["MongoDB:DatabaseName"] ?? "signalstack";
builder.Services.AddMongoMigrations(mongoConnectionString, mongoDatabaseName);

// Persist ASP.NET Core Data Protection keys in Redis so they survive container
// restarts (the warning about ephemeral keys is resolved). Without this, auth
// cookies, CSRF tokens, and antiforgery tokens are invalidated on every deploy.
// REQ-SEC-001: Data Protection key ring must survive container restarts.
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? builder.Configuration["Redis:ConnectionString"];
if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
  var redis = ConnectionMultiplexer.Connect(redisConnectionString);
  builder.Services.AddDataProtection()
      .SetApplicationName("SignalStack.Api")
      .PersistKeysToStackExchangeRedis(() => redis.GetDatabase(), "DataProtection-Keys");
}

// Trade-ledger write lock primitives (REQ-PORT-031/031a/031b)
builder.Services.AddLedgerWriteLock();

// Trade-ledger repository and ingestion pipeline (P5-T9 — REQ-PORT-005a, REQ-PORT-023, REQ-RECON-001..004)
builder.Services.AddTradeLedgerServices();

// Trade-ledger writer services (P5-T8 lock wiring — REQ-PORT-031/031a/031b)
// Ingestion logic wired in P5-T9.
builder.Services.AddSingleton<OnLoginSyncService>();
builder.Services.AddSingleton<ManualAdjustmentService>();

// Portfolio and reconciliation services — P5-T10 / REQ-RECON-001..004
builder.Services.AddPortfolioServices();

// RME advisory and risk services — P6-T27 / REQ-CHART-* (advisory subset), RME-L1
builder.Services.AddRiskServices();

// Execution assistance services — P7-T3 / pre-flight check endpoint
builder.Services.AddExecutionServices();

// OAuth / JWT Bearer / CSRF — REQ-AUTH-001/002/011, REQ-SEC-001
builder.Services.AddSignalStackAuth(builder.Configuration);

// Server-side session management — REQ-SESSION-001/002/002a
builder.Services.AddSessionManagement();

// User identity model, roles, approval state, tester ceiling — REQ-ROLE-001..004, REQ-LEGAL-003
builder.Services.AddUserManagement();

// Immutable audit event collection — REQ-SEC-011, REQ-CONFIG-005a
builder.Services.AddAuditEventManagement(); // registered via SignalStack.Storage.Audit

// FYERS credential management + token lifecycle — REQ-AUTH-003..010, 014, REQ-SESSION-009
// Also registers HttpClient for FYERS REST API fallback (REQ-MARKET-002b).
builder.Services.AddFyersTokenManagement();
builder.Services.AddSingleton<FyersAuthService>();
builder.Services.AddHttpClient("FyersApi", client =>
{
  client.Timeout = TimeSpan.FromSeconds(30);
});

// PLD WebSocket session lease — REQ-SESSION-014
builder.Services.AddPldWebSocketServices();

// Phase A constraint enforcement services — REQ-LEGAL-002
builder.Services.AddPhaseConstraintServices();

// Phase transition gating service — P8-T3 / REQ-LEGAL-001/005/005a
builder.Services.AddSingleton<PhaseGateService>();

// Penetration test scheduling + remediation tracking — P8-T10 / REQ-SEC-010
builder.Services.AddSingleton<IPenetrationTestRepository, MongoPenetrationTestRepository>();

// Chaos / failure-injection exercise recording — P8-T12 / REQ-NFR-014
builder.Services.AddSingleton<IChaosExerciseRepository, MongoChaosExerciseRepository>();

// Data subject rights workflow — REQ-PRIVACY-004 (P2-T21)
builder.Services.AddPrivacyRequestManagement();

// Symbol master + universe state management — P3-T2 / REQ-UNIV-001..010, REQ-HIST-005..008a
// SQL Server connection string for sync health checking — P3-T6 / REQ-UNIV-015a
var sqlConnectionString = builder.Configuration.GetConnectionString("SqlServer");
builder.Services.AddUniverseManagement(sqlConnectionString);

// Trading calendar management — P3-T3 / REQ-CALENDAR-001..006
builder.Services.AddTradingCalendarManagement();

// Time Stop recompute service — P6-T18 / REQ-STOP-003a
// Triggered by calendar CRUD endpoints; debounced, fire-and-forget.
builder.Services.AddSingleton<TimeStopRecomputeService>();

// Signal Subscription management — P4-T1 / REQ-STRAT-007a/007b, REQ-STRAT-017b
builder.Services.AddSignalSubscriptionManagement();

// Historical OHLCV data services — P3-T11 / REQ-HIST-001..011, REQ-HIST-010a
// Registers ISymbolTableMapping (single shared mapping) and IOhlcvRepository (SQL Server).
builder.Services.AddHistoricalServices(sqlConnectionString);

// Backtesting engine + result persistence — P4-T3 / REQ-STRAT-011b/023
builder.Services.AddBacktestingServices(sqlConnectionString);

// Notification collection schema + writer paths — P5-T3 / REQ-NOTIFY-006
// REQ-NOTIFY-004: synchronous persistence before Telegram delivery.
builder.Services.AddNotificationServices();

// Push notification WebSocket fan-out infrastructure — P5-T13 / REQ-NFR-013
// Registers PushConnectionManager, PushFanOutService (Redis pub/sub subscriber).
// REQ-DATA-006a(c)(i): degraded fallback when Redis is unreachable.
builder.Services.AddPushNotificationServices();

// Telegram bot provisioning + deep-link subscription + token rotation — P5-T5
// REQ-NOTIFY-015/016/017/022a.
builder.Services.AddTelegramBotServices();

// Sentinel startup check: fails when sys_config seeder has not been run (REQ-CONFIG-010).
builder.Services.AddHealthChecks()
    .AddCheck<SeedVersionHealthCheck>("seed_version_sentinel",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["readyz"]);
builder.Services.AddRateLimiter(options =>
{
  options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
  options.OnRejected = static async (context, cancellationToken) =>
  {
    context.HttpContext.Response.Headers["Retry-After"] = "60";
    await context.HttpContext.Response.WriteAsJsonAsync(
      new ErrorResponse(
        "rate_limit_exceeded",
        "Authentication request rate limit exceeded."),
      cancellationToken: cancellationToken);
  };

  options.AddPolicy(AuthRateLimitPolicy, httpContext =>
  {
    var key = httpContext.Connection.RemoteIpAddress?.ToString();
    if (string.IsNullOrWhiteSpace(key))
    {
      key = "unknown";
    }

    return RateLimitPartition.GetFixedWindowLimiter(
      partitionKey: key,
      factory: _ => new FixedWindowRateLimiterOptions
      {
        PermitLimit = 10,
        Window = TimeSpan.FromHours(1),
        QueueLimit = 0,
        AutoReplenishment = true
      });
  });
});

// CORS — allows the frontend origin(s) configured in Cors:AllowedOrigins.
// Required on Railway since there is no platform-level CORS panel.
var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (corsAllowedOrigins is { Length: > 0 })
{
  builder.Services.AddCors(options =>
  {
    options.AddDefaultPolicy(policy =>
      {
        policy.WithOrigins(corsAllowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
      });
  });
}

var app = builder.Build();

// Railway terminates TLS at the edge and forwards plain HTTP to the container.
// This middleware ensures Request.Scheme reflects the original protocol.
// X-Forwarded-Proto is read when available; in production we also apply a hard
// default since Railway always terminates TLS.
app.Use((context, next) =>
{
  if (context.Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto)
      && proto.ToString().Equals("https", StringComparison.OrdinalIgnoreCase))
  {
    context.Request.Scheme = "https";
  }
  else if (!app.Environment.IsDevelopment())
  {
    context.Request.Scheme = "https";
  }
  return next(context);
});

if (!app.Environment.IsDevelopment())
{
  app.UseHsts();
}

var cspConnectSrc = builder.Configuration["Security:Csp:ConnectSrc"]
    ?? "'self'";

app.Use(async (context, next) =>
{
  context.Response.Headers["Content-Security-Policy"] =
        $"default-src 'none'; "
        + $"frame-ancestors 'none'; "
        + $"base-uri 'self'; "
        + $"connect-src {cspConnectSrc}; "
        + $"img-src 'self' data:; "
        + $"style-src 'self' 'unsafe-inline';";
  context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
  context.Response.Headers["X-Content-Type-Options"] = "nosniff";
  context.Response.Headers["X-Frame-Options"] = "DENY";
  context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
  context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
  context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
  context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";

  await next();
});

// WebSocket support for browser-tier PLD stream — REQ-SESSION-014.
app.UseWebSockets(new WebSocketOptions
{
  KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.UseRateLimiter();

// Diagnostic logging for OAuth callback/complete requests.
app.Use(async (context, next) =>
{
  var path = context.Request.Path.Value ?? "";
  if (path.Contains("/api/v1/auth/callback") || path.Contains("/api/v1/auth/complete"))
  {
    var logger = context.RequestServices
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("SignalStack.Api.Auth");
    var state = context.Request.Query["state"].FirstOrDefault() ?? "";
    var code = context.Request.Query["code"].FirstOrDefault() ?? "";
    var err = context.Request.Query["error"].FirstOrDefault() ?? "";
    logger.LogWarning(
        "OAuth callback: Scheme={Scheme} Host={Host} Path={Path} State={SLen} Code={CLen} Error={Error} XFP={XFP}",
        context.Request.Scheme, context.Request.Host, context.Request.Path,
        state.Length, code.Length, err,
        context.Request.Headers["X-Forwarded-Proto"].FirstOrDefault());
  }
  await next(context);
});

// CORS — handles preflight (OPTIONS) requests before auth so the OAuth
// login flow from the frontend origin is not rejected.
if (corsAllowedOrigins is { Length: > 0 })
  app.UseCors();

// Auth middleware — must precede CsrfMiddleware so context.User is populated.
app.UseAuthentication();
app.UseAuthorization();

// Session validation: rejects requests whose JWT jti is no longer in the
// sessions collection (REQ-SESSION-002: new login invalidates prior session).
app.UseMiddleware<SessionValidationMiddleware>();

// CSRF enforcement on authenticated mutations — REQ-SEC-001 defence-in-depth.
// Disabled in Testing environment: integration tests use Bearer tokens not cookies,
// so CSRF validation would reject all test mutation requests with 403 Forbidden.
if (!app.Environment.IsEnvironment("Testing"))
  app.UseMiddleware<CsrfMiddleware>();
// Propagate browser correlation ID (X-Correlation-Id) onto the OpenTelemetry
// span so frontend API calls can be joined with backend traces in Honeycomb.
// Also tag the authenticated user identity on every span for trace filtering.
app.Use(async (context, next) =>
{
  if (context.Request.Headers.TryGetValue("X-Correlation-Id", out var cid))
  {
    Activity.Current?.SetTag("web.correlation_id", cid.ToString());
  }

  // Enrich the auto-created HTTP span with authenticated user context
  if (context.User?.Identity?.IsAuthenticated == true)
  {
    Activity.Current?.SetTag("enduser.id",
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value);
    Activity.Current?.SetTag("enduser.role",
        context.User.FindFirst(ClaimTypes.Role)?.Value);
  }

  await next();
});

// Record API request metrics (duration, status code) for all endpoints.
// Uses ApiTelemetry meters registered with the OTLP exporter.
app.Use(async (context, next) =>
{
  var sw = Stopwatch.StartNew();
  try
  {
    await next();
  }
  finally
  {
    var elapsedMs = sw.Elapsed.TotalMilliseconds;
    var method = context.Request.Method;
    var statusCode = context.Response.StatusCode;
    var path = context.Request.Path.Value ?? "/";

    ApiTelemetry.RequestDurationMs.Record(elapsedMs,
        new KeyValuePair<string, object?>("method", method),
        new KeyValuePair<string, object?>("path", path),
        new KeyValuePair<string, object?>("status_code", statusCode));

    ApiTelemetry.RequestTotal.Add(1,
        new KeyValuePair<string, object?>("method", method),
        new KeyValuePair<string, object?>("status_code", statusCode));
  }
});

// Admin impersonation write-rejection middleware — REQ-ADMIN-015.
// Must run after SessionValidationMiddleware so the session is loaded,
// and before all mutation endpoints.
app.UseMiddleware<ImpersonationMiddleware>();

// Phase A constraint enforcement — REQ-LEGAL-002.
// Blocks billing/payment endpoints with HTTP 403 during Phase A;
// passes through when phase is B or C.
app.UseMiddleware<PhaseEnforcementMiddleware>();

app.Use(async (context, next) =>
{
  await next();

  var logger = context.RequestServices
    .GetRequiredService<ILoggerFactory>()
    .CreateLogger("SignalStack.Api.Http");

  var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

  using (logger.BeginScope(new Dictionary<string, object?>
  {
    ["service"] = ApiServiceName,
    ["environment"] = app.Environment.EnvironmentName,
    ["trace"] = traceId
  }))
  {
    logger.LogInformation(
      "HTTP {Method} {Path} responded {StatusCode}",
      context.Request.Method,
      context.Request.Path.Value ?? "/",
      context.Response.StatusCode);
  }
});

app.MapGet("/api/v1", () => Results.Ok(new ApiRootResponse(ApiServiceName, "v0.5")));

// Signal Subscription endpoints — P4-T1 / REQ-STRAT-007a/007b, REQ-STRAT-017b
app.MapSignalSubscriptionEndpoints();
app.MapHealthChecks("/api/v1/healthz");
app.MapHealthChecks("/api/v1/readyz", new()
{
  Predicate = check => check.Tags.Contains("readyz")
});
app.MapGroup("/api/v1/auth")
  .RequireRateLimiting(AuthRateLimitPolicy)
  .MapPost("/probe", () => Results.Ok(new AuthProbeResponse("ok")));

// Auth endpoints: providers, login, callback, csrf, me — P2-T1
app.MapAuthEndpoints(app.Environment);

// FYERS credential management + token lifecycle endpoints — P2-T7
app.MapFyersEndpoints();

// PLD WebSocket session lease endpoint — REQ-SESSION-014
app.MapPldEndpoints();

// Push notification WebSocket endpoint — P5-T13 / REQ-NFR-013
app.MapPushEndpoints();

// Admin sys_config management endpoints — P2-T11 / REQ-CONFIG-005/005a/007, REQ-SEC-011
app.MapAdminConfigEndpoints();

// Admin user management endpoints — P2-T12 / REQ-ROLE-004, REQ-SESSION-012/013
app.MapAdminUserEndpoints();

// Admin privacy request (DSAR) endpoints — P2-T21 / REQ-PRIVACY-004
app.MapAdminPrivacyRequestEndpoints();

// Admin data breach endpoints — P2-T22 / REQ-PRIVACY-006
app.MapAdminBreachEndpoints();

// Admin home / transfer recovery summary endpoints — P2-T19 / REQ-ROLE-007a
app.MapAdminHomeEndpoints();

// Admin global RME kill switch endpoints — P6-T25 / REQ-ADMIN-007
app.MapAdminKillSwitchEndpoints();

// Admin signal type enable/disable endpoints — P6-T25 / REQ-ADMIN-011/013
app.MapAdminSignalTypeEndpoints();

// Admin position management endpoints (suspend/release/force-close) — P6-T25 / REQ-ADMIN-016, REQ-PLC-005a
app.MapAdminPositionEndpoints();

// Admin incident dashboard endpoints — P6-T26 / REQ-RME-CONC-007
app.MapAdminIncidentEndpoints();

// RME advisory endpoints — P6-T27 / REQ-CHART-* (advisory subset), RME-L1
app.MapRmeAdvisoryEndpoints();

// Execution assistance endpoints — P7-T3 / pre-flight check endpoint
app.MapExecutionEndpoints();

// Admin audit event query endpoints — P8-T1 / helper APIs for audit_events collection
app.MapAdminAuditEndpoints();

// Admin system health dashboard endpoints — P8-T2 / REQ-ADMIN-014, A-14
app.MapAdminSystemHealthEndpoints();

// Admin phase transition + gate management endpoints — P8-T3 / REQ-LEGAL-001/005/005a
app.MapAdminPhaseEndpoints();

// Admin Legal Posture widget endpoint — P8-T4 / REQ-LEGAL-010
app.MapAdminLegalPostureEndpoints();

// Admin impersonation endpoints — P8-T5 / REQ-ADMIN-015, REQ-PRIVACY-012
app.MapImpersonationEndpoints();

// Admin penetration test management endpoints — P8-T10 / REQ-SEC-010
app.MapAdminPenetrationTestEndpoints();

// Admin chaos / failure-injection exercise endpoints — P8-T12 / REQ-NFR-014
app.MapAdminChaosExerciseEndpoints();

// Telegram bot provisioning + management endpoints (admin) — P5-T5 / REQ-NOTIFY-015/022a
app.MapTelegramBotEndpoints();

// User Telegram linking endpoints — P5-T5 / REQ-NOTIFY-016/017/018
app.MapTelegramLinkingEndpoints();

// User profile settings endpoints — P6-T6 / REQ-RME-006c
app.MapUserProfileEndpoints();

// Notification feed endpoints — P5-T6 / REQ-NOTIFY-014, REQ-PROFILE-006
app.MapNotificationEndpoints();

// Reconciliation endpoints — P5-T10 / REQ-RECON-001..004
app.MapReconciliationEndpoints();

// Portfolio endpoints — P5-T11 / REQ-DASH-002/010 (holdings/PnL subset)
app.MapPortfolioEndpoints();

// Symbol master / universe endpoints — P3-T2 / REQ-UNIV-001..010
app.MapGroup("/api/v1/universe").MapSymbolMasterEndpoints();

// Universe sync endpoints (admin) — P3-T5 / REQ-UNIV-011..020
app.MapUniverseSyncEndpoints();

// Trading calendar endpoints — P3-T3 / REQ-CALENDAR-001..006
app.MapTradingCalendarEndpoints();

// Chart data endpoints — P3-T11 / REQ-HIST-001..011, REQ-HIST-010a
app.MapChartEndpoints();

// Backtest endpoints — P4-T3 / REQ-STRAT-011b/023
app.MapGroup("/api/v1/backtest").RequireAuthorization().MapBacktestingEndpoints();

// Initialize historical services: load symbol-to-table-name mapping from symbol master.
// REQ-HIST-011: mapping is loaded in memory at startup.
await app.InitializeHistoricalServicesAsync();

// Phase 1: Expose OpenAPI spec at /openapi/v1.json
app.MapOpenApi();

app.Run();

public sealed record ApiRootResponse(string Name, string Version);

public sealed record ErrorResponse(string Error, string Message);

public sealed record AuthProbeResponse(string Status);

public partial class Program;
