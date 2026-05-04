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

  // ── Order callback reliability counters (P7-T10 / REQ-ORDER-015d) ─────
  // order_callback_received_total lives in ApiTelemetry (API handles callbacks).

  public static readonly Counter<long> OrderReconciledViaLadsOnlyTotal = Meter.CreateCounter<long>(
      "order_reconciled_via_lads_only_total",
      description: "Count of intents reconciled by LADS without a callback (callback-missed path).");

  public static readonly Counter<long> OrderUnresolvedTotal = Meter.CreateCounter<long>(
      "order_unresolved_total",
      description: "Count of intents that timed out unresolved without a matching FYERS order.");

  public static readonly Counter<long> OrderOrphanTotal = Meter.CreateCounter<long>(
      "order_orphan_total",
      description: "Count of orphan FYERS trades with no matching platform intent.");
}
