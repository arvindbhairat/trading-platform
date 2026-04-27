using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SignalStack.Configuration.Bootstrap;

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

    var resourceBuilder = ResourceBuilder.CreateDefault()
      .AddService(serviceName: serviceName, serviceVersion: "v0.2")
      .AddAttributes(new Dictionary<string, object>
      {
        ["deployment.environment"] = builder.Environment.EnvironmentName,
        ["signalstack.telemetry.endpoint"] = telemetryOptions.GetActiveEndpoint().ToString()
      });

    builder.Logging.AddOpenTelemetry(logging =>
    {
      logging.IncludeFormattedMessage = true;
      logging.IncludeScopes = true;
      logging.ParseStateValues = true;
      logging.SetResourceBuilder(resourceBuilder);
      logging.AddOtlpExporter(exporter =>
      {
        exporter.Endpoint = telemetryOptions.GetActiveEndpoint();
        exporter.Protocol = OtlpExportProtocol.Grpc;
        exporter.TimeoutMilliseconds = telemetryOptions.ExportTimeoutMilliseconds;
      });
    });

    builder.Services.AddOpenTelemetry()
      .ConfigureResource(resource => resource.AddService(serviceName: serviceName, serviceVersion: "v0.2"))
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
