using System.Diagnostics;
using SignalStack.Api.Observability;
using SignalStack.Configuration.Bootstrap;

const string ApiServiceName = "SignalStack.Api";

var builder = WebApplication.CreateBuilder(args);

builder.AddSignalStackBootstrapConfiguration(ApiServiceName);
builder.AddSignalStackTelemetry(ApiServiceName);
builder.Services.AddHealthChecks();

var app = builder.Build();

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

app.Run();

public sealed record ApiRootResponse(string Name, string Version);

public partial class Program;

