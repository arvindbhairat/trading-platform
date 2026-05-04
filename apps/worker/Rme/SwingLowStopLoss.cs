namespace SignalStack.Worker.Rme;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Swing Low Stop Loss — places the stop below the most recent swing low price.
/// Uses <see cref="StopLossInput.SwingLowPrice"/> directly (first-class field on the
/// input record) with a configurable buffer percentage via
/// <see cref="RmeModuleOptions.DefaultSwingLowBufferPct"/> to keep the stop strictly
/// below the swing low level.
/// Falls back to the default fixed distance percentage when swing low is unavailable.
/// (portfolio-risk-guidelines § Stop Loss Types — Required Stop Types (V1)).
/// Stateless and thread-safe; register as singleton.
/// </summary>
internal sealed class SwingLowStopLoss : IStopLoss
{
    public string Name => "SwingLowStop";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<SwingLowStopLoss> _logger;

    public SwingLowStopLoss(
        IOptions<RmeModuleOptions> options,
        ILogger<SwingLowStopLoss> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StopLossResult Calculate(StopLossInput input)
    {
        decimal stopPrice;

        // Attempt to read an explicit SwingLowStopPrice from AdditionalInputs.
        if (input.AdditionalInputs?.TryGetValue("SwingLowStopPrice", out var priceObj) == true
            && priceObj is decimal explicitPrice)
        {
            stopPrice = explicitPrice;
            _logger.LogDebug(
                "SwingLowStop: using explicit stop price {StopPrice} from additional inputs.",
                stopPrice);
        }
        else if (input.SwingLowPrice is > 0m && input.SwingLowPrice < input.EntryPrice)
        {
            // Swing low is valid and below entry — place stop below it with buffer.
            var bufferPct = (decimal)_options.DefaultSwingLowBufferPct / 100m;
            stopPrice = input.SwingLowPrice.Value * (1m - bufferPct);

            _logger.LogDebug(
                "SwingLowStop: computed stop {StopPrice} from swing low {SwingLow}, " +
                "buffer {BufferPct}%, entry {EntryPrice}.",
                stopPrice, input.SwingLowPrice.Value, _options.DefaultSwingLowBufferPct,
                input.EntryPrice);
        }
        else
        {
            // Fallback: compute stop price from the default percentage distance.
            var distancePct = (decimal)_options.DefaultFixedStopDistancePct / 100m;
            stopPrice = input.EntryPrice * (1m - distancePct);

            _logger.LogWarning(
                "SwingLowStop: SwingLowPrice missing or invalid (value: {SwingLowPrice}); " +
                "falling back to default {DistPct}% distance.",
                input.SwingLowPrice, _options.DefaultFixedStopDistancePct);
        }

        // Guard: for a long-only platform the stop must be below entry.
        if (stopPrice >= input.EntryPrice)
        {
            _logger.LogWarning(
                "SwingLowStop: computed stop {StopPrice} is not below entry {EntryPrice}. " +
                "Clamping to entry price minus minimum distance (1%).",
                stopPrice, input.EntryPrice);
            stopPrice = input.EntryPrice * 0.99m;
        }

        return new StopLossResult(
            StopPrice: Math.Round(stopPrice, 2),
            StopTypeUsed: Name);
    }
}
