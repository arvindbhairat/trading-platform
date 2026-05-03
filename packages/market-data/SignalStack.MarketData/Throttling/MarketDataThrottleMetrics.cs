using System.Diagnostics.Metrics;

namespace SignalStack.MarketData.Throttling;

/// <summary>
/// OpenTelemetry-compatible instruments for market data provider throttle metrics.
///
/// Metrics are emitted through <see cref="System.Diagnostics.Metrics.Meter"/> and
/// picked up automatically by the OpenTelemetry MeterListener when the
/// <c>OpenTelemetry.Instrumentation.Runtime</c> or a custom MeterProvider is
/// configured with this meter's name (<c>SignalStack.MarketData.Throttling</c>).
/// </summary>
public sealed class MarketDataThrottleMetrics : IDisposable
{
    internal const string MeterName = "SignalStack.MarketData.Throttling";

    private readonly Meter _meter;
    private bool _disposed;

    /// <summary>
    /// Total number of API calls made through the throttle (counter).
    /// Tags: provider (e.g., "fyers"), status ("success", "throttled", "error").
    /// </summary>
    public Counter<long> ApiCallsTotal { get; }

    /// <summary>
    /// Current remaining daily budget for the provider (observable gauge).
    /// Tag: provider.
    /// </summary>
    private readonly ObservableGauge<long> _remainingBudgetGauge;
    private Func<long> _remainingBudgetProvider = static () => 0;

    /// <summary>
    /// Current daily usage percentage (observable gauge).
    /// Tag: provider.
    /// </summary>
    private readonly ObservableGauge<double> _dailyUsagePercentGauge;
    private Func<double> _dailyUsagePercentProvider = static () => 0.0;

    /// <summary>
    /// Queue wait duration in milliseconds for throttle acquisition (histogram).
    /// Tag: provider.
    /// </summary>
    public Histogram<double> ThrottleQueueWaitMs { get; }

    /// <summary>
    /// Number of 429 responses received (counter).
    /// Tag: provider.
    /// </summary>
    public Counter<long> ThrottledResponsesTotal { get; }

    /// <summary>
    /// Number of budget-exhaustion events (counter).
    /// Tag: provider.
    /// </summary>
    public Counter<long> BudgetExhaustionTotal { get; }

    public MarketDataThrottleMetrics(string providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
            throw new ArgumentException("Provider name is required.", nameof(providerName));

        _meter = new Meter(MeterName);

        ApiCallsTotal = _meter.CreateCounter<long>(
            "mdp.api.calls.total",
            description: "Total number of API calls made through the throttle.",
            unit: "{calls}");

        _remainingBudgetGauge = _meter.CreateObservableGauge(
            "mdp.budget.remaining",
            () => new Measurement<long>(_remainingBudgetProvider(), new KeyValuePair<string, object?>("provider", providerName)),
            description: "Remaining daily API call budget.",
            unit: "{calls}");

        _dailyUsagePercentGauge = _meter.CreateObservableGauge(
            "mdp.budget.usage_percent",
            () => new Measurement<double>(_dailyUsagePercentProvider(), new KeyValuePair<string, object?>("provider", providerName)),
            description: "Daily API budget usage percentage.",
            unit: "%");

        ThrottleQueueWaitMs = _meter.CreateHistogram<double>(
            "mdp.throttle.queue_wait_ms",
            description: "Queue wait duration for throttle permit acquisition.",
            unit: "ms");

        ThrottledResponsesTotal = _meter.CreateCounter<long>(
            "mdp.throttle.retries.total",
            description: "Total number of 429 retries.",
            unit: "{retries}");

        BudgetExhaustionTotal = _meter.CreateCounter<long>(
            "mdp.budget.exhaustion.total",
            description: "Total number of daily budget exhaustion events.",
            unit: "{events}");
    }

    public void SetBudgetProviders(Func<long> remainingBudget, Func<double> usagePercent)
    {
        _remainingBudgetProvider = remainingBudget ?? throw new ArgumentNullException(nameof(remainingBudget));
        _dailyUsagePercentProvider = usagePercent ?? throw new ArgumentNullException(nameof(usagePercent));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _meter.Dispose();
    }
}
