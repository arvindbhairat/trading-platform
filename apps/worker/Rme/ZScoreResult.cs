namespace SignalStack.Worker.Rme;

/// <summary>
/// Advisory result from the z-score overlay service. Indicates whether the
/// current z-score deviation produces an add-more or reduce-size advisory,
/// and maps the thresholds to price levels for user-facing display.
/// REQ-SIZING-006, REQ-SIZING-007, REQ-SIZING-008.
/// </summary>
public sealed record ZScoreAdvisory
{
    /// <summary>Computed z-score from the rolling window of closing prices.</summary>
    public required double ZScore { get; init; }

    /// <summary>Configured add-more threshold (default: +3). REQ-SIZING-007.</summary>
    public required double AddThreshold { get; init; }

    /// <summary>Configured reduce-size threshold (default: -3). REQ-SIZING-007.</summary>
    public required double ReduceThreshold { get; init; }

    /// <summary>Timeframe label (e.g. "rolling_5d").</summary>
    public required string Timeframe { get; init; }

    /// <summary>Number of closing prices used in the computation.</summary>
    public required int SampleCount { get; init; }

    /// <summary>Mean of the closing prices in the sample.</summary>
    public double Mean { get; init; }

    /// <summary>Standard deviation of the closing prices in the sample.</summary>
    public double StandardDeviation { get; init; }

    /// <summary>
    /// The price level that would correspond to the add-more threshold.
    /// mean + (addThreshold × stdDev). Null when stdDev is zero.
    /// REQ-SIZING-008.
    /// </summary>
    public decimal? AddPriceLevel { get; init; }

    /// <summary>
    /// The price level that would correspond to the reduce-size threshold.
    /// mean + (reduceThreshold × stdDev). Null when stdDev is zero.
    /// REQ-SIZING-008.
    /// </summary>
    public decimal? ReducePriceLevel { get; init; }

    /// <summary>
    /// True when the z-score is at or above the add threshold,
    /// suggesting an add-more opportunity.
    /// </summary>
    public bool AddAdvisory => ZScore >= AddThreshold;

    /// <summary>
    /// True when the z-score is at or below the reduce threshold,
    /// suggesting a reduce-size action.
    /// </summary>
    public bool ReduceAdvisory => ZScore <= ReduceThreshold;

    /// <summary>Human-readable advisory message, or null when neither threshold is breached.</summary>
    public string? AdvisoryMessage { get; init; }
}
