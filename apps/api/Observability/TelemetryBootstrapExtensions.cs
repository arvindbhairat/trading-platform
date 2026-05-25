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

    if (hasOtlpEndpoint)
    {
      otelBuilder
        .WithLogging(
          logging => logging
            .AddProcessor(new SensitiveDataRedactionLogProcessor())
            .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetryOptions)),
          options =>
          {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
          })
        .WithTracing(tracing => tracing
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation()
          .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetryOptions)))
        .WithMetrics(metrics => metrics
          .AddAspNetCoreInstrumentation()
          .AddHttpClientInstrumentation()
          .AddRuntimeInstrumentation()
          .AddMeter(serviceName)
          .AddMeter(ConfigurationBootstrapTelemetry.MeterName)
          .AddOtlpExporter(exporter => ConfigureExporter(exporter, telemetryOptions)));
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

  private static void ConfigureExporter(OtlpExporterOptions exporter, OtlpTelemetryOptions options)
  {
    exporter.Endpoint = new Uri(options.Endpoint);
    exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
    exporter.TimeoutMilliseconds = options.ExportTimeoutMilliseconds;

    if (!string.IsNullOrWhiteSpace(options.ApiKey))
    {
      exporter.Headers = $"api-key={options.ApiKey}";
    }
  }
}
