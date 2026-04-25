using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SignalStack.Worker.Observability;

internal static class WorkerTelemetry
{
  public const string ServiceName = "SignalStack.Worker";

  public static readonly ActivitySource ActivitySource = new(ServiceName);
  public static readonly Meter Meter = new(ServiceName);
  public static readonly Counter<long> HeartbeatCounter = Meter.CreateCounter<long>("worker.heartbeat.count");
  public static readonly Histogram<double> LeaseRefreshDurationMilliseconds = Meter.CreateHistogram<double>("worker.singleton.lease_refresh.duration_ms");
}
