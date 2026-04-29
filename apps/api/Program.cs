using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using SignalStack.Api.Observability;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Migrations;

const string ApiServiceName = "SignalStack.Api";
const string AuthRateLimitPolicy = "auth-fixed-window";

var builder = WebApplication.CreateBuilder(args);

builder.AddSignalStackBootstrapConfiguration(ApiServiceName);
builder.AddSignalStackTelemetry(ApiServiceName);

var mongoConnectionString = builder.Configuration.GetConnectionString("MongoDb");
var mongoDatabaseName = builder.Configuration["MongoDB:DatabaseName"] ?? "signalstack";
builder.Services.AddMongoMigrations(mongoConnectionString, mongoDatabaseName);

builder.Services.AddHealthChecks();
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

app.Use(async (context, next) =>
{
  context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'self'";
  context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
  context.Response.Headers["X-Content-Type-Options"] = "nosniff";
  context.Response.Headers["X-Frame-Options"] = "DENY";
  context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
  context.Response.Headers["Cross-Origin-Opener-Policy"] = "same-origin";
  context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
  context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains; preload";

  await next();
});

app.UseRateLimiter();

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

app.MapGet("/api/v1", () => Results.Ok(new ApiRootResponse(ApiServiceName, "v0.2")));
app.MapHealthChecks("/api/v1/healthz");
app.MapHealthChecks("/api/v1/readyz");
app.MapGroup("/api/v1/auth")
  .RequireRateLimiting(AuthRateLimitPolicy)
  .MapPost("/probe", () => Results.Ok(new { status = "ok" }));

app.Run();

public sealed record ApiRootResponse(string Name, string Version);

public partial class Program;

