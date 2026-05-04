using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SignalStack.Api.Admin;
using SignalStack.Api.Audit;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Auth;
using SignalStack.Api.Fyers;
using SignalStack.Api.Observability;
using SignalStack.Api.PhaseEnforcement;
using SignalStack.Api.Pld;
using SignalStack.Api.PrivacyRequest;
using SignalStack.Api.DataBreach;
using SignalStack.Api.Sessions;
using SignalStack.Api.Universe;
using SignalStack.Api.Users;
using SignalStack.Api.Historical;
using SignalStack.Api.Backtesting;
using SignalStack.Api.Signals;
using SignalStack.Api.Notifications;
using SignalStack.Api.Push;
using SignalStack.Api.TelegramBot;
using SignalStack.Api.LedgerWriters;
using SignalStack.Api.Portfolio;
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

var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDb");
var mongoDatabaseName = builder.Configuration["MongoDB:DatabaseName"] ?? "signalstack";
builder.Services.AddMongoMigrations(mongoConnectionString, mongoDatabaseName);

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

// OAuth / JWT Bearer / CSRF — REQ-AUTH-001/002/011, REQ-SEC-001
builder.Services.AddSignalStackAuth(builder.Configuration);

// Server-side session management — REQ-SESSION-001/002/002a
builder.Services.AddSessionManagement();

// User identity model, roles, approval state, tester ceiling — REQ-ROLE-001..004, REQ-LEGAL-003
builder.Services.AddUserManagement();

// Immutable audit event collection — REQ-SEC-011, REQ-CONFIG-005a
builder.Services.AddAuditEventManagement();

// FYERS credential management + token lifecycle — REQ-AUTH-003..010, 014, REQ-SESSION-009
// Also registers HttpClient for FYERS REST API fallback (REQ-MARKET-002b).
builder.Services.AddFyersTokenManagement();
builder.Services.AddHttpClient("FyersApi", client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});

// PLD WebSocket session lease — REQ-SESSION-014
builder.Services.AddPldWebSocketServices();

// Phase A constraint enforcement services — REQ-LEGAL-002
builder.Services.AddPhaseConstraintServices();

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
builder.Services.AddTimeStopRecomputeService();

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
      new
      {
        error = "rate_limit_exceeded",
        message = "Authentication request rate limit exceeded."
      },
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

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
  ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

if (!app.Environment.IsEnvironment("Testing"))
{
  app.UseHttpsRedirection();
}

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

// Auth middleware — must precede CsrfMiddleware so context.User is populated.
app.UseAuthentication();
app.UseAuthorization();

// Session validation: rejects requests whose JWT jti is no longer in the
// sessions collection (REQ-SESSION-002: new login invalidates prior session).
app.UseMiddleware<SessionValidationMiddleware>();

// CSRF enforcement on authenticated mutations — REQ-SEC-001 defence-in-depth.
app.UseMiddleware<CsrfMiddleware>();

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
  .MapPost("/probe", () => Results.Ok(new { status = "ok" }));

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

app.Run();

public sealed record ApiRootResponse(string Name, string Version);

public partial class Program;
