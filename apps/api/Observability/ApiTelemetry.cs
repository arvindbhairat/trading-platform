using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace SignalStack.Api.Observability;

/// <summary>
/// OpenTelemetry instruments for the SignalStack API service.
/// Meters are auto-collected by the OTLP exporter configured in
/// <see cref="TelemetryBootstrapExtensions.AddSignalStackTelemetry"/>.
/// </summary>
internal static class ApiTelemetry
{
    public const string ServiceName = "SignalStack.Api";

    /// <summary>
    /// Activity source for manual tracing of API operations.
    /// Registered via AddSource() in TelemetryBootstrapExtensions.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new(ServiceName);

    public static readonly Meter Meter = new(ServiceName);

    // ── Order callbacks (REQ-ORDER-015d) ─────────────────────────────────

    /// <summary>
    /// Counter: FYERS widget callbacks received by the platform.
    /// Tagged by status (matched, submission_failed) and user_class (test, production).
    /// </summary>
    public static readonly Counter<long> OrderCallbackReceivedTotal = Meter.CreateCounter<long>(
        "order_callback_received_total",
        description: "Count of FYERS widget callbacks received (success or failure).");

    // ── API request metrics ──────────────────────────────────────────────

    /// <summary>
    /// Histogram: duration of API handler execution in milliseconds.
    /// Tagged by method, path template, status code.
    /// </summary>
    public static readonly Histogram<double> RequestDurationMs = Meter.CreateHistogram<double>(
        "api.http.request.duration_ms",
        unit: "ms",
        description: "Duration of API request handler execution.");

    /// <summary>
    /// Counter: total API responses by status code family.
    /// Tagged by method, status_code.
    /// </summary>
    public static readonly Counter<long> RequestTotal = Meter.CreateCounter<long>(
        "api.http.request.total",
        description: "Total API responses by status code family.");

    // ── Auth metrics (REQ-AUTH-001..002) ─────────────────────────────────

    /// <summary>
    /// Counter: successful logins by provider.
    /// Tagged by provider (google, microsoft, facebook).
    /// </summary>
    public static readonly Counter<long> AuthLoginSuccessTotal = Meter.CreateCounter<long>(
        "api.auth.login.success.total",
        description: "Successful logins by OAuth provider.");

    /// <summary>
    /// Counter: failed login attempts.
    /// Tagged by provider and reason.
    /// </summary>
    public static readonly Counter<long> AuthLoginFailedTotal = Meter.CreateCounter<long>(
        "api.auth.login.failed.total",
        description: "Failed login attempts by provider and reason.");

    // ── Execution metrics (REQ-ORDER-015) ────────────────────────────────

    /// <summary>
    /// Counter: pre-flight checks performed.
    /// Tagged by outcome (pass, fail) and symbol.
    /// </summary>
    public static readonly Counter<long> PreFlightCheckTotal = Meter.CreateCounter<long>(
        "api.execution.pre_flight_check.total",
        description: "Pre-flight checks performed.");

    /// <summary>
    /// Counter: signed payload requests.
    /// Tagged by user_class.
    /// </summary>
    public static readonly Counter<long> SignedPayloadTotal = Meter.CreateCounter<long>(
        "api.execution.signed_payload.total",
        description: "Signed payload requests.");

    // ── Portfolio metrics (REQ-DASH-002) ─────────────────────────────────

    /// <summary>
    /// Histogram: portfolio data retrieval duration.
    /// </summary>
    public static readonly Histogram<double> PortfolioFetchDurationMs = Meter.CreateHistogram<double>(
        "api.portfolio.fetch.duration_ms",
        unit: "ms",
        description: "Duration of portfolio data retrieval.");

    // ── Session metrics (REQ-SESSION-001..002) ───────────────────────────

    /// <summary>
    /// Counter: session validation outcomes.
    /// Tagged by result (valid, missing_jti, revoked).
    /// </summary>
    public static readonly Counter<long> SessionValidationTotal = Meter.CreateCounter<long>(
        "api.session.validation.total",
        description: "Session validation outcomes.");
}
