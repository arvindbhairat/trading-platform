using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Computes z-score deviation advisories from rolling candle closing prices.
/// Maps threshold breaches to price levels for user-facing add-more and
/// reduce-size suggestions.
///
/// Formula:
///   z = (latestPrice - mean) / stdDev
///   addPriceLevel = mean + (addThreshold × stdDev)
///   reducePriceLevel = mean + (reduceThreshold × stdDev)
///
/// Stateless and thread-safe; register as singleton.
/// REQ-SIZING-006, REQ-SIZING-007, REQ-SIZING-008.
/// </summary>
internal sealed class ZScoreAdvisoryService : IZScoreAdvisoryService
{
    private readonly RmeModuleOptions _options;
    private readonly ILogger<ZScoreAdvisoryService> _logger;

    public ZScoreAdvisoryService(
        IOptions<RmeModuleOptions> options,
        ILogger<ZScoreAdvisoryService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ZScoreAdvisory ComputeAdvisory(
        IReadOnlyList<decimal> closingPrices,
        string timeframe = "rolling_5d")
    {
        // ── Step 1: Validate input ─────────────────────────────────────────────
        if (closingPrices is null || closingPrices.Count < 2)
        {
            _logger.LogWarning(
                "ZScoreAdvisoryService: insufficient closing prices ({Count}) for z-score computation. " +
                "Returning neutral advisory.",
                closingPrices?.Count ?? 0);

            return new ZScoreAdvisory
            {
                ZScore = 0d,
                AddThreshold = _options.ZScoreAddThreshold,
                ReduceThreshold = _options.ZScoreReduceThreshold,
                Timeframe = timeframe,
                SampleCount = 0,
                AdvisoryMessage = "Insufficient price data for z-score computation."
            };
        }

        // ── Step 2: Compute mean and standard deviation ────────────────────────
        var prices = closingPrices.Select(p => (double)p).ToArray();
        var mean = prices.Average();
        var variance = prices.Sum(p => Math.Pow(p - mean, 2)) / prices.Length;
        var stdDev = Math.Sqrt(variance);

        if (stdDev < 0.0001d)
        {
            _logger.LogDebug(
                "ZScoreAdvisoryService: standard deviation is near zero ({StdDev}). " +
                "Returning neutral advisory.",
                stdDev);

            return new ZScoreAdvisory
            {
                ZScore = 0d,
                AddThreshold = _options.ZScoreAddThreshold,
                ReduceThreshold = _options.ZScoreReduceThreshold,
                Timeframe = timeframe,
                SampleCount = closingPrices.Count,
                Mean = mean,
                StandardDeviation = 0d,
                AddPriceLevel = null,
                ReducePriceLevel = null,
                AdvisoryMessage = "Price standard deviation is near zero; no z-score deviation detected."
            };
        }

        // ── Step 3: Compute z-score for the latest price ───────────────────────
        var latestPrice = (double)closingPrices[^1];
        var zScore = (latestPrice - mean) / stdDev;

        // ── Step 4: Map thresholds to price levels (REQ-SIZING-008) ────────────
        var addPriceLevel = (decimal)(mean + _options.ZScoreAddThreshold * stdDev);
        var reducePriceLevel = (decimal)(mean + _options.ZScoreReduceThreshold * stdDev);

        // ── Step 5: Build advisory message ─────────────────────────────────────
        string? advisoryMessage = null;

        if (zScore >= _options.ZScoreAddThreshold)
        {
            advisoryMessage =
                $"Z-score of {zScore:F2} is at or above the add threshold " +
                $"(+{_options.ZScoreAddThreshold:F1}). " +
                $"Price would need to fall to ₹{reducePriceLevel:F2} for a reduce signal. " +
                $"Timeframe: {timeframe} ({closingPrices.Count} sessions).";
        }
        else if (zScore <= _options.ZScoreReduceThreshold)
        {
            advisoryMessage =
                $"Z-score of {zScore:F2} is at or below the reduce threshold " +
                $"({_options.ZScoreReduceThreshold:F1}). " +
                $"Price would need to rise to ₹{addPriceLevel:F2} for an add signal. " +
                $"Timeframe: {timeframe} ({closingPrices.Count} sessions).";
        }
        else
        {
            _logger.LogDebug(
                "ZScoreAdvisoryService: z-score {ZScore:F2} within normal range " +
                "[{ReduceThreshold:F1}, {AddThreshold:F1}]. No advisory.",
                zScore, _options.ZScoreReduceThreshold, _options.ZScoreAddThreshold);
        }

        return new ZScoreAdvisory
        {
            ZScore = zScore,
            AddThreshold = _options.ZScoreAddThreshold,
            ReduceThreshold = _options.ZScoreReduceThreshold,
            Timeframe = timeframe,
            SampleCount = closingPrices.Count,
            Mean = mean,
            StandardDeviation = stdDev,
            AddPriceLevel = addPriceLevel,
            ReducePriceLevel = reducePriceLevel,
            AdvisoryMessage = advisoryMessage,
        };
    }
}
