using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Configuration.Logging;
using Serilog;

namespace SignalStack.Api.Observability;

internal static class TelemetryBootstrapExtensions
{
  public static void AddSignalStackTelemetry(this WebApplicationBuilder builder, string serviceName)
  {
    var telemetryOptions = builder.Configuration
      .GetSection(OtlpTelemetryOptions.SectionName)
      .Get<OtlpTelemetryOptions>()
      ?? new OtlpTelemetryOptions();

    builder.Services.Configure<OtlpTelemetryOptions>(
      builder.Configuration.GetSection(OtlpTelemetryOptions.SectionName));

    var serilogLogger = new LoggerConfiguration()
      .ConfigureSignalStackLogger(
        builder.Configuration,
        serviceName,
        builder.Environment.EnvironmentName,
        sinks => sinks.Console())
      .CreateLogger();

    builder.Logging.ClearProviders();
    builder.Logging.AddSerilog(serilogLogger, dispose: true);

    var otelBuilder = builder.Services.AddOpenTelemetry()
      .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: "v0.2")
        .AddAttributes(new Dictionary<string, object>
        {
          ["deployment.environment"] = builder.Environment.EnvironmentName
        }));

    bool hasOtlpEndpoint = !string.IsNullOrWhiteSpace(telemetryOptions.Endpoint);

    serilogLogger.Information(
      "Telemetry config — Endpoint: {Endpoint}, ApiKey configured: {HasKey}, Timeout: {Timeout}ms",
      hasOtlpEndpoint ? telemetryOptions.Endpoint : "(not set)",
      !string.IsNullOrWhiteSpace(telemetryOptions.ApiKey),
      telemetryOptions.ExportTimeoutMilliseconds);

    // Connection strings (presence only — values masked)
    serilogLogger.Information(
      "Connection strings — MongoDb: {HasMongoDb}, SqlServer: {HasSqlServer}, Redis: {HasRedis}, Database: {DatabaseName}",
      !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("MongoDb")),
      !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("SqlServer")),
      !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Redis")
        ?? builder.Configuration["Redis:ConnectionString"]),
      builder.Configuration["MongoDB:DatabaseName"] ?? "signalstack");

    // Auth providers
    serilogLogger.Information(
      "Auth config — JWT secret: {HasJwtSecret}, Google: {HasGoogle}, Microsoft: {HasMicrosoft}, Facebook: {HasFacebook}",
      !string.IsNullOrWhiteSpace(builder.Configuration["Auth:Jwt:Secret"]),
      !string.IsNullOrWhiteSpace(builder.Configuration["Auth:Google:ClientId"]),
      !string.IsNullOrWhiteSpace(builder.Configuration["Auth:Microsoft:ClientId"]),
      !string.IsNullOrWhiteSpace(builder.Configuration["Auth:Facebook:AppId"]));

    // Bootstrap snapshot paths
    serilogLogger.Information(
      "Bootstrap config — AppConfig: {AppConfigPath}, KeyVault: {KeyVaultPath}, LKG: {LkgPath}",
      builder.Configuration["SignalStack:Bootstrap:AppConfiguration:SnapshotPath"] ?? "(not set)",
      builder.Configuration["SignalStack:Bootstrap:KeyVault:SnapshotPath"] ?? "(not set)",
      builder.Configuration["SignalStack:Bootstrap:LastKnownGood:CachePath"] ?? "(not set)");

    if (hasOtlpEndpoint)
    {
      otelBuilder
        .WithLogging(
          logging => logging
            .AddProcessor(new SensitiveDataRedactionLogProcessor())
            .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetryOptions, "v1/logs")),
          options =>
          {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
          })
        .WithTracing(tracing => tracing
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation()
          .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetryOptions, "v1/traces")))
        .WithMetrics(metrics => metrics
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation()
          .AddRuntimeInstrumentation()
          .AddMeter(serviceName)
          .AddMeter(ConfigurationBootstrapTelemetry.MeterName)
          .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetryOptions, "v1/metrics")));
    }
    else
    {
      otelBuilder
        .WithLogging()
        .WithTracing(tracing => tracing
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation())
        .WithMetrics(metrics => metrics
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation()
          .AddRuntimeInstrumentation()
          .AddMeter(serviceName)
          .AddMeter(ConfigurationBootstrapTelemetry.MeterName));
    }
  }

  private static void ConfigureExporter(OtlpExporterOptions exporter, OtlpTelemetryOptions options, string signalPath)
  {
    // Append the signal path (/v1/logs, /v1/traces, /v1/metrics) to the base
    // endpoint. The OTel .NET SDK does not append signal paths when Endpoint is
    // explicitly set — without this, requests go to the root URL and return 404.
    exporter.Endpoint = new Uri(new Uri(options.Endpoint), signalPath);
    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    exporter.TimeoutMilliseconds = options.ExportTimeoutMilliseconds;

    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
      exporter.Headers = $"Authorization=Basic {options.ApiKey}";
    }
  }
}
