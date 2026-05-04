namespace SignalStack.Worker.Rme;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// ATR-based Stop Loss — stop set at a multiple of ATR below entry price.
/// Uses <see cref="StopLossInput.AtrValue"/> directly (first-class field on the input record)
/// and <see cref="RmeModuleOptions.AtrMultiplier"/> to compute the stop distance.
/// Falls back to the default fixed distance percentage when ATR is unavailable.
/// (portfolio-risk-guidelines § Stop Loss Types — Required Stop Types (V1)).
/// Stateless and thread-safe; register as singleton.
/// </summary>
internal sealed class AtrStopLoss : IStopLoss
{
    public string Name => "AtrStop";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<AtrStopLoss> _logger;

    public AtrStopLoss(
        IOptions<RmeModuleOptions> options,
        ILogger<AtrStopLoss> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StopLossResult Calculate(StopLossInput input)
    {
        decimal stopPrice;

        // Attempt to read an explicit AtrStopPrice from AdditionalInputs.
        if (input.AdditionalInputs?.TryGetValue("AtrStopPrice", out var priceObj) == true
            && priceObj is decimal explicitPrice)
        {
            stopPrice = explicitPrice;
            _logger.LogDebug(
                "AtrStop: using explicit stop price {StopPrice} from additional inputs.",
                stopPrice);
        }
        else if (input.AtrValue is > 0m)
        {
            // Compute stop distance as ATR × multiplier, then subtract from entry.
            var atrMultiplier = (decimal)_options.AtrMultiplier;
            var stopDistance = input.AtrValue.Value * atrMultiplier;
            stopPrice = input.EntryPrice - stopDistance;

            _logger.LogDebug(
                "AtrStop: computed stop {StopPrice} from entry {EntryPrice}, " +
                "ATR {AtrValue}, multiplier {Multiplier}.",
                stopPrice, input.EntryPrice, input.AtrValue.Value, atrMultiplier);
        }
        else
        {
            // Fallback: compute stop price from the default percentage distance.
            var distancePct = (decimal)_options.DefaultFixedStopDistancePct / 100m;
            stopPrice = input.EntryPrice * (1m - distancePct);

            _logger.LogWarning(
                "AtrStop: ATR value missing or invalid (value: {AtrValue}); " +
                "falling back to default {DistPct}% distance.",
                input.AtrValue, _options.DefaultFixedStopDistancePct);
        }

        // Guard: for a long-only platform the stop must be below entry.
        if (stopPrice >= input.EntryPrice)
        {
            _logger.LogWarning(
                "AtrStop: computed stop {StopPrice} is not below entry {EntryPrice}. " +
                "Clamping to entry price minus minimum distance (1%).",
                stopPrice, input.EntryPrice);
            stopPrice = input.EntryPrice * 0.99m;
        }

        return new StopLossResult(
            StopPrice: Math.Round(stopPrice, 2),
            StopTypeUsed: Name);
    }
}
