using System.Diagnostics.Metrics;

namespace SignalStack.Configuration.Bootstrap;

public static class ConfigurationBootstrapTelemetry
{
  public const string MeterName = "SignalStack.Configuration";
  public const string LastReachedSecondsGaugeName = "config.source.last_reached_seconds";

  private static readonly Meter Meter = new(MeterName);

  public static void RegisterMetrics(ConfigurationBootstrapState state)
  {
    Meter.CreateObservableGauge(
      LastReachedSecondsGaugeName,
      () => new Measurement<double>(
        state.GetSecondsSinceLastSuccessfulRead(DateTimeOffset.UtcNow),
        new KeyValuePair<string, object?>("service", state.ServiceName),
        new KeyValuePair<string, object?>("source", state.SourceKind.ToString())));
  }
}
