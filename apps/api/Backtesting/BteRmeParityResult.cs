namespace SignalStack.Api.Backtesting;

/// <summary>
/// Result of a single BTE-vs-RME parity check run.
/// REQ-NFR-017: divergence metrics for the nightly parity check.
/// </summary>
public sealed record BteRmeParityResult
{
    /// <summary>Whether the parity check passed overall (no threshold-exceeding divergences).</summary>
    public bool Passed { get; init; }

    /// <summary>UTC timestamp of the check.</summary>
    public DateTime CheckTimestamp { get; init; }

    /// <summary>Number of test scenarios evaluated.</summary>
    public int ScenariosEvaluated { get; init; }

    /// <summary>Number of scenarios where divergence exceeded the threshold.</summary>
    public int ScenariosWithDivergence { get; init; }

    /// <summary>Maximum stop price divergence observed (as % of entry price).</summary>
    public decimal MaxStopPriceDivergencePct { get; init; }

    /// <summary>Maximum position size divergence observed (as % of reference).</summary>
    public decimal MaxSizingDivergencePct { get; init; }

    /// <summary>Configured divergence threshold used for this run.</summary>
    public decimal DivergenceThresholdPct { get; init; }

    /// <summary>Per-scenario divergence details when any exceeded the threshold.</summary>
    public List<ScenarioDivergenceDetail>? Divergences { get; init; }

    /// <summary>Human-readable summary for the admin notification.</summary>
    public string Summary { get; init; } = "";
}

/// <summary>Per-scenario divergence detail for audit trail and notification content.</summary>
public sealed record ScenarioDivergenceDetail
{
    /// <summary>Label identifying the scenario (e.g. "fixed_stop_2pct").</summary>
    public string ScenarioLabel { get; init; } = "";

    /// <summary>The metric that diverged: "stop_price" | "position_size".</summary>
    public string Metric { get; init; } = "";

    /// <summary>Value computed by the RME config parsing (what backtest would use).</summary>
    public decimal ComputedValue { get; init; }

    /// <summary>Expected reference value from the RME model.</summary>
    public decimal ReferenceValue { get; init; }

    /// <summary>Absolute divergence as a percentage of the reference value.</summary>
    public decimal DivergencePct { get; init; }
}
