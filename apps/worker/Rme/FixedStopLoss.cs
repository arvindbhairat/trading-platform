namespace SignalStack.Worker.Rme;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Fixed Stop Loss — stop set at a fixed price below entry.
/// Reads <c>FixedStopPrice</c> from <see cref="StopLossInput.AdditionalInputs"/>;
/// falls back to <see cref="RmeModuleOptions.DefaultFixedStopDistancePct"/>
/// calculated against entry price when no explicit price is provided.
/// (portfolio-risk-guidelines § Stop Loss Types — Required Stop Types (V1)).
/// All models are stateless and must be registered as singletons.
/// </summary>
internal sealed class FixedStopLoss : IStopLoss
{
    public string Name => "FixedStop";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<FixedStopLoss> _logger;

    public FixedStopLoss(
        IOptions<RmeModuleOptions> options,
        ILogger<FixedStopLoss> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StopLossResult Calculate(StopLossInput input)
    {
        decimal stopPrice;

        // Attempt to read an explicit FixedStopPrice from AdditionalInputs.
        if (input.AdditionalInputs?.TryGetValue("FixedStopPrice", out var priceObj) == true
            && priceObj is decimal fixedPrice)
        {
            stopPrice = fixedPrice;
            _logger.LogDebug(
                "FixedStop: using explicit stop price {StopPrice} from additional inputs.",
                stopPrice);
        }
        else
        {
            // Fallback: compute stop price from the default percentage distance.
            var distancePct = (decimal)_options.DefaultFixedStopDistancePct / 100m;
            stopPrice = input.EntryPrice * (1m - distancePct);
            _logger.LogDebug(
                "FixedStop: no explicit stop price provided; computed default {StopPrice} " +
                "from entry {EntryPrice} at {DistPct}% distance.",
                stopPrice, input.EntryPrice, _options.DefaultFixedStopDistancePct);
        }

        // Guard: for a long-only platform the stop must be below entry.
        if (stopPrice >= input.EntryPrice)
        {
            _logger.LogWarning(
                "FixedStop: computed stop {StopPrice} is not below entry {EntryPrice}. " +
                "Clamping to entry price minus minimum distance (1%).",
                stopPrice, input.EntryPrice);
            stopPrice = input.EntryPrice * 0.99m;
        }

        return new StopLossResult(
            StopPrice: Math.Round(stopPrice, 2),
            StopTypeUsed: Name);
    }
}
