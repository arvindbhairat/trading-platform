namespace SignalStack.Worker.Rme;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Trailing Stop Loss (percentage-based) — the stop trails behind the
/// highest price observed since entry by a fixed percentage.
///
/// Reads <c>TrailingStopPercent</c> from the dedicated input field (or
/// <see cref="RmeModuleOptions.DefaultTrailingStopPercent"/> as fallback)
/// and <c>HighestPriceSinceEntry</c> from <see cref="StopLossInput.AdditionalInputs"/>.
/// Falls back to entry price when no highest price is provided.
///
/// The caller (trailing-stop sync service) is responsible for ensuring the
/// stop price is non-decreasing across recalculations (ADR-0003 carve-out).
/// (portfolio-risk-guidelines § Stop Loss Types — Required Stop Types (V1)).
/// Stateless and thread-safe; register as singleton.
/// </summary>
internal sealed class TrailingStopLoss : IStopLoss
{
    public string Name => "TrailingStop";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<TrailingStopLoss> _logger;

    public TrailingStopLoss(
        IOptions<RmeModuleOptions> options,
        ILogger<TrailingStopLoss> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StopLossResult Calculate(StopLossInput input)
    {
        // ── Step 1: Resolve trailing percentage ──────────────────────────
        var trailingPct = input.TrailingStopPercent ?? (decimal)_options.DefaultTrailingStopPercent;

        if (trailingPct <= 0m)
        {
            _logger.LogWarning(
                "TrailingStop: invalid or zero trailing percentage {TrailingPct}. " +
                "Falling back to configured default {DefaultPct}%.",
                trailingPct, _options.DefaultTrailingStopPercent);

            trailingPct = (decimal)_options.DefaultTrailingStopPercent;
        }

        // ── Step 2: Resolve highest price since entry ─────────────────────
        decimal highestPriceSinceEntry;

        if (input.AdditionalInputs?.TryGetValue("HighestPriceSinceEntry", out var priceObj) == true
            && priceObj is decimal explicitPrice)
        {
            highestPriceSinceEntry = explicitPrice;

            _logger.LogDebug(
                "TrailingStop: using explicit highest price {HighestPrice} from additional inputs.",
                highestPriceSinceEntry);
        }
        else
        {
            // Fallback to entry price when no highest price is available.
            highestPriceSinceEntry = input.EntryPrice;

            _logger.LogDebug(
                "TrailingStop: no highest price provided; using entry price {EntryPrice} " +
                "as baseline.",
                input.EntryPrice);
        }

        // Prevent negative or zero highest price from producing invalid results.
        if (highestPriceSinceEntry <= 0m)
        {
            _logger.LogWarning(
                "TrailingStop: highest price {HighestPrice} is <= 0. Using entry price {EntryPrice}.",
                highestPriceSinceEntry, input.EntryPrice);

            highestPriceSinceEntry = input.EntryPrice;
        }

        // ── Step 3: Compute the candidate stop price ─────────────────────
        var trailingFactor = 1m - (trailingPct / 100m);
        var stopPrice = highestPriceSinceEntry * trailingFactor;

        _logger.LogDebug(
            "TrailingStop: computed stop {StopPrice} from highest {HighestPrice}, " +
            "trailing {TrailingPct}%.",
            stopPrice, highestPriceSinceEntry, trailingPct);

        // ── Step 4: Guard against degenerate results ──────────────────────
        // Unlike fixed/ATR stops, a trailing stop MAY be above entry price
        // (the price has moved up and the stop trails behind it). We only
        // clamp against the highest price used in the computation.
        if (stopPrice >= highestPriceSinceEntry)
        {
            _logger.LogWarning(
                "TrailingStop: computed stop {StopPrice} is not below highest price " +
                "{HighestPrice}. Clamping to highest price minus minimum distance (1%).",
                stopPrice, highestPriceSinceEntry);

            stopPrice = highestPriceSinceEntry * 0.99m;
        }

        // Final sanity: stop must be positive.
        if (stopPrice <= 0m)
        {
            _logger.LogWarning(
                "TrailingStop: computed stop {StopPrice} is <= 0. Clamping to nominal minimum.",
                stopPrice);
            stopPrice = 0.01m;
        }

        return new StopLossResult(
            StopPrice: Math.Round(stopPrice, 2),
            StopTypeUsed: Name);
    }
}
