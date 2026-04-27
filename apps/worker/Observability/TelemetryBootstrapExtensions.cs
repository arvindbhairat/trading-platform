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

    builder.Services.AddOpenTelemetry()
      .ConfigureResource(resource => resource
        .AddService(serviceName: serviceName, serviceVersion: "v0.2")
        .AddAttributes(new Dictionary<string, object>
        {
          ["deployment.environment"] = builder.Environment.EnvironmentName,
          ["signalstack.telemetry.endpoint"] = telemetryOptions.GetActiveEndpoint().ToString()
        }))
      .WithLogging(
        logging => logging
          .AddProcessor(new SensitiveDataRedactionLogProcessor())
          .AddOtlpExporter(exporter =>
          {
            exporter.Endpoint = telemetryOptions.GetActiveEndpoint();
            exporter.Protocol = OtlpExportProtocol.Grpc;
            exporter.TimeoutMilliseconds = telemetryOptions.ExportTimeoutMilliseconds;
          }),
        options =>
        {
          options.IncludeFormattedMessage = true;
          options.IncludeScopes = true;
          options.ParseStateValues = true;
        })
      .WithTracing(tracing => tracing
        .AddSource(WorkerTelemetry.ActivitySource.Name)
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(exporter =>
        {
          exporter.Endpoint = telemetryOptions.GetActiveEndpoint();
          exporter.Protocol = OtlpExportProtocol.Grpc;
          exporter.TimeoutMilliseconds = telemetryOptions.ExportTimeoutMilliseconds;
        }))
      .WithMetrics(metrics => metrics
        .AddRuntimeInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter(WorkerTelemetry.Meter.Name)
        .AddMeter(ConfigurationBootstrapTelemetry.MeterName)
        .AddOtlpExporter(exporter =>
        {
          exporter.Endpoint = telemetryOptions.GetActiveEndpoint();
          exporter.Protocol = OtlpExportProtocol.Grpc;
          exporter.TimeoutMilliseconds = telemetryOptions.ExportTimeoutMilliseconds;
        }));
  }
}
