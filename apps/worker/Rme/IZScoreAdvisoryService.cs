namespace SignalStack.Worker.Rme;

/// <summary>
/// Z-score deviation-based advisory overlay on top of the primary sizing model.
/// Computes z-scores from rolling candle closing prices and determines whether
/// an add-more or reduce-size advisory should be surfaced, with mapped price
/// levels for user-facing display.
///
/// REQ-SIZING-006, REQ-SIZING-007, REQ-SIZING-008.
/// </summary>
public interface IZScoreAdvisoryService
{
    /// <summary>
    /// Compute the z-score advisory for a sequence of closing prices.
    /// The caller provides the closing prices in chronological order (oldest first).
    /// Returns advisory results with threshold breaches and mapped price levels.
    /// REQ-SIZING-006, REQ-SIZING-007.
    /// </summary>
    ZScoreAdvisory ComputeAdvisory(IReadOnlyList<decimal> closingPrices, string timeframe = "rolling_5d");
}
