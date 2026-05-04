namespace SignalStack.Worker.Rme;

/// <summary>
/// Evaluates pyramiding (add-on) and scaling (reduce) levels for an open position.
/// Computes advisory signals based on configured triggers (fixed % move, R multiples,
/// ATR multiples), enforces the never-lowers-stop floor invariant (REQ-PYR-011),
/// and produces a combined stop price across all tranches.
/// REQ-PYR-001..011, REQ-SIZING-007.
/// </summary>
public interface IPyramidingAdvisoryService
{
    /// <summary>
    /// Evaluate whether an add-on (pyramid entry) or reduce (scale-out) is
    /// recommended at the current price for the given position.
    /// </summary>
    PyramidingAdvisory Evaluate(PyramidingEvaluationInput input);

    /// <summary>
    /// Compute the combined stop price across a set of tranches, enforcing the
    /// never-lowers-stop floor invariant (REQ-PYR-011).
    ///
    /// The combined stop is the quantity-weighted average of individual tranche
    /// stops. If this average is lower than the highest individual tranche stop
    /// currently in effect, the highest existing stop is used as the floor.
    /// </summary>
    decimal ComputeCombinedStop(
        IReadOnlyList<TrancheRecord> tranches,
        decimal? stopFloor = null,
        decimal? currentCombinedStop = null);

    /// <summary>
    /// Compute the quantity-weighted average entry price across all tranches.
    /// REQ-PYR-010.
    /// </summary>
    decimal ComputeAverageEntryPrice(IReadOnlyList<TrancheRecord> tranches);

    /// <summary>
    /// Compute the current R multiple for a position.
    /// R = (CurrentPrice - AverageEntryPrice) / (AverageEntryPrice - CombinedStopPrice)
    /// When the stop distance is zero, R multiple is undefined and null is returned.
    /// REQ-PYR-010.
    /// </summary>
    decimal? ComputeRMultiple(
        decimal currentPrice,
        decimal averageEntryPrice,
        decimal combinedStopPrice);

    /// <summary>
    /// Compute the recommended add-on quantity, which reduces progressively
    /// with each successive add (REQ-PYR-004).
    /// </summary>
    decimal ComputeAddonQuantity(decimal baseQuantity, int addLevel, double sizeReductionPct);
}
