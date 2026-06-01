using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SignalStack.Configuration.Bootstrap;
using SignalStack.Configuration.Logging;
using Serilog;

namespace SignalStack.Worker.Observability;

internal static class TelemetryBootstrapExtensions
{
  public static void AddSignalStackTelemetry(this IHostApplicationBuilder builder, string serviceName)
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

    // Bootstrap snapshot paths
    serilogLogger.Information(
      "Bootstrap config — AppConfig: {AppConfigPath}, KeyVault: {KeyVaultPath}, LKG: {LkgPath}",
      builder.Configuration["SignalStack:Bootstrap:AppConfiguration:SnapshotPath"] ?? "(not set)",
      builder.Configuration["SignalStack:Bootstrap:KeyVault:SnapshotPath"] ?? "(not set)",
      builder.Configuration["SignalStack:Bootstrap:LastKnownGood:CachePath"] ?? "(not set)");

    // Worker singleton lease
    serilogLogger.Information(
      "Worker singleton — LeaseKey: {LeaseKey}, TTL: {LeaseTtl}s, RefreshInterval: {RefreshInterval}s, HeartbeatInterval: {HeartbeatInterval}s",
      builder.Configuration["WorkerSingleton:LeaseKey"] ?? "rme:worker:singleton",
      builder.Configuration.GetValue<int>("WorkerSingleton:LeaseTtlSeconds", 60),
      builder.Configuration.GetValue<int>("WorkerSingleton:RefreshIntervalSeconds", 20),
      builder.Configuration.GetValue<int>("WorkerSingleton:HeartbeatIntervalSeconds", 10));

    // Job poll intervals
    serilogLogger.Information(
      "Job poll intervals (s) — DataSync: {DataSync}, HistoricDataSeed: {Hds}, LiveMarketScan: {Lms}, NotificationDelivery: {Nd}, AccountSync: {Acct}, EodSignalRunner: {Eod}, AdminFyersTokenCheck: {FyersCheck}",
      builder.Configuration.GetValue<int>("DataSync:PollIntervalSeconds", 120),
      builder.Configuration.GetValue<int>("HistoricDataSeed:PollIntervalSeconds", 30),
      builder.Configuration.GetValue<int>("LiveMarketScan:PollIntervalSeconds", 90),
      builder.Configuration.GetValue<int>("NotificationDelivery:PollIntervalSeconds", 10),
      builder.Configuration.GetValue<int>("AccountSync:PollIntervalSeconds", 60),
      builder.Configuration.GetValue<int>("EodSignalRunner:PollIntervalSeconds", 300),
      builder.Configuration.GetValue<int>("AdminFyersTokenCheck:PollIntervalSeconds", 60));

    // Enable Npgsql built-in OpenTelemetry tracing (Npgsql 9.0+).
    // Adds db.system, db.name, db.statement attributes to spans for every
    // NpgsqlCommand execution with zero code changes.
    AppContext.SetSwitch("Npgsql.EnableTelemetry", true);

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
          .AddSource(WorkerTelemetry.ActivitySource.Name)
          .AddHttpClientInstrumentation()
          .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetryOptions, "v1/traces")))
        .WithMetrics(metrics => metrics
          .AddRuntimeInstrumentation()
          .AddHttpClientInstrumentation()
          .AddMeter(WorkerTelemetry.Meter.Name)
          .AddMeter(ConfigurationBootstrapTelemetry.MeterName)
          .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetryOptions, "v1/metrics")));
    }
    else
    {
      otelBuilder
        .WithLogging()
        .WithTracing(tracing => tracing
          .AddSource(WorkerTelemetry.ActivitySource.Name)
          .AddHttpClientInstrumentation())
        .WithMetrics(metrics => metrics
          .AddRuntimeInstrumentation()
          .AddHttpClientInstrumentation()
          .AddMeter(WorkerTelemetry.Meter.Name)
          .AddMeter(ConfigurationBootstrapTelemetry.MeterName));
    }
  }

  private static void ConfigureExporter(OtlpExporterOptions exporter, OtlpTelemetryOptions options, string signalPath)
  {
    exporter.Endpoint = new Uri(new Uri(options.Endpoint), signalPath);
    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    exporter.TimeoutMilliseconds = options.ExportTimeoutMilliseconds;

    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
      exporter.Headers = $"x-honeycomb-team={options.ApiKey}";
    }
  }
}
