using System.Diagnostics.Metrics;

namespace SignalStack.Api.Observability;

/// <summary>
/// OpenTelemetry instruments for the SignalStack API service.
/// Meters are auto-collected by the OTLP exporter configured in
/// <see cref="TelemetryBootstrapExtensions.AddSignalStackTelemetry"/>.
/// </summary>
internal static class ApiTelemetry
{
    public static readonly Meter Meter = new("SignalStack.Api");

    /// <summary>
    /// Counter: FYERS widget callbacks received by the platform.
    /// Tagged by status (matched, submission_failed) and user_class (test, production).
    /// REQ-ORDER-015d.
    /// </summary>
    public static readonly Counter<long> OrderCallbackReceivedTotal = Meter.CreateCounter<long>(
        "order_callback_received_total",
        description: "Count of FYERS widget callbacks received (success or failure).");
}
