namespace SignalStack.Worker.Rme;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Trailing Stop Loss (ATR-based) — the stop trails behind the highest price
/// observed since entry by an ATR-based distance (<c>AtrValue</c> ×
/// <see cref="RmeModuleOptions.AtrMultiplier"/>).
///
/// Combines the ATR stop-loss distance model with the trailing-stop semantics:
/// the stop price is computed relative to the highest price since entry (not the
/// entry price), so the stop moves up as the price rises. When ATR is unavailable,
/// falls back to the configured default fixed distance percentage.
///
/// The caller (trailing-stop sync service) is responsible for ensuring the stop
/// price is non-decreasing across recalculations (ADR-0003 carve-out).
/// (portfolio-risk-guidelines § Stop Loss Types — Required Stop Types (V1)).
/// Stateless and thread-safe; register as singleton.
/// </summary>
internal sealed class AtrTrailingStopLoss : IStopLoss
{
    public string Name => "AtrTrailingStop";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<AtrTrailingStopLoss> _logger;

    public AtrTrailingStopLoss(
        IOptions<RmeModuleOptions> options,
        ILogger<AtrTrailingStopLoss> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StopLossResult Calculate(StopLossInput input)
    {
        // ── Step 1: Resolve highest price since entry ─────────────────────
        decimal highestPriceSinceEntry;

        if (input.AdditionalInputs?.TryGetValue("HighestPriceSinceEntry", out var priceObj) == true
            && priceObj is decimal explicitPrice)
        {
            highestPriceSinceEntry = explicitPrice;

            _logger.LogDebug(
                "AtrTrailingStop: using explicit highest price {HighestPrice} from additional inputs.",
                highestPriceSinceEntry);
        }
        else
        {
            highestPriceSinceEntry = input.EntryPrice;

            _logger.LogDebug(
                "AtrTrailingStop: no highest price provided; using entry price {EntryPrice} " +
                "as baseline.",
                input.EntryPrice);
        }

        if (highestPriceSinceEntry <= 0m)
        {
            _logger.LogWarning(
                "AtrTrailingStop: highest price {HighestPrice} is <= 0. Using entry price {EntryPrice}.",
                highestPriceSinceEntry, input.EntryPrice);

            highestPriceSinceEntry = input.EntryPrice;
        }

        // ── Step 2: Compute the trailing stop price ──────────────────────
        decimal stopPrice;

        if (input.AtrValue is > 0m)
        {
            // ATR-based trail distance from the highest price since entry.
            var atrMultiplier = (decimal)_options.AtrMultiplier;
            var trailDistance = input.AtrValue.Value * atrMultiplier;
            stopPrice = highestPriceSinceEntry - trailDistance;

            _logger.LogDebug(
                "AtrTrailingStop: computed stop {StopPrice} from highest {HighestPrice}, " +
                "ATR {AtrValue}, multiplier {Multiplier}.",
                stopPrice, highestPriceSinceEntry, input.AtrValue.Value, atrMultiplier);
        }
        else
        {
            // Fallback: compute stop price as a fixed percentage below the
            // highest price since entry.
            var distancePct = (decimal)_options.DefaultFixedStopDistancePct / 100m;
            stopPrice = highestPriceSinceEntry * (1m - distancePct);

            _logger.LogWarning(
                "AtrTrailingStop: ATR value missing or invalid (value: {AtrValue}); " +
                "falling back to default {DistPct}% distance from highest price.",
                input.AtrValue, _options.DefaultFixedStopDistancePct);
        }

        // ── Step 3: Guard against degenerate results ──────────────────────
        // A trailing stop should always be below the highest price used as
        // its baseline. If the calculation produces a stop at or above the
        // highest price, clamp to highest price minus a minimum distance.
        if (stopPrice >= highestPriceSinceEntry)
        {
            _logger.LogWarning(
                "AtrTrailingStop: computed stop {StopPrice} is not below highest price " +
                "{HighestPrice}. Clamping to highest price minus minimum distance (1%).",
                stopPrice, highestPriceSinceEntry);

            stopPrice = highestPriceSinceEntry * 0.99m;
        }

        // Final sanity: stop must be positive.
        if (stopPrice <= 0m)
        {
            _logger.LogWarning(
                "AtrTrailingStop: computed stop {StopPrice} is <= 0. Clamping to nominal minimum.",
                stopPrice);
            stopPrice = 0.01m;
        }

        return new StopLossResult(
            StopPrice: Math.Round(stopPrice, 2),
            StopTypeUsed: Name);
    }
}
