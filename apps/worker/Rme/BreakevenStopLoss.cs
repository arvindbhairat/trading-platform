namespace SignalStack.Worker.Rme;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Break-Even Stop Loss — moves the stop to the entry price once the position
/// reaches a configurable profit level (default 1R).
/// Reads <c>CurrentRMultiple</c> from <see cref="StopLossInput.AdditionalInputs"/>;
/// compares against <see cref="RmeModuleOptions.DefaultBreakevenTriggerR"/>.
/// Falls back to the default fixed distance percentage when the trigger condition
/// is not yet met or R-multiple data is unavailable.
/// (portfolio-risk-guidelines § Stop Loss Types — Required Stop Types (V1)).
/// Stateless and thread-safe; register as singleton.
/// </summary>
internal sealed class BreakevenStopLoss : IStopLoss
{
    public string Name => "BreakevenStop";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<BreakevenStopLoss> _logger;

    public BreakevenStopLoss(
        IOptions<RmeModuleOptions> options,
        ILogger<BreakevenStopLoss> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public StopLossResult Calculate(StopLossInput input)
    {
        // Read the current R-multiple from additional inputs.
        if (input.AdditionalInputs?.TryGetValue("CurrentRMultiple", out var rObj) == true
            && rObj is decimal currentR)
        {
            var triggerR = (decimal)_options.DefaultBreakevenTriggerR;

            if (currentR >= triggerR)
            {
                _logger.LogDebug(
                    "BreakevenStop: triggered at {CurrentR}R (trigger: {TriggerR}R). " +
                    "Moving stop to entry {EntryPrice}.",
                    currentR, triggerR, input.EntryPrice);

                return new StopLossResult(
                    StopPrice: Math.Round(input.EntryPrice, 2),
                    StopTypeUsed: Name);
            }

            _logger.LogDebug(
                "BreakevenStop: not triggered — current R {CurrentR}R < trigger {TriggerR}R. " +
                "Falling back to default distance.",
                currentR, triggerR);
        }
        else
        {
            _logger.LogWarning(
                "BreakevenStop: CurrentRMultiple not available in additional inputs. " +
                "Falling back to default distance.");
        }

        // Fallback: compute stop price from the default percentage distance.
        var distancePct = (decimal)_options.DefaultFixedStopDistancePct / 100m;
        var stopPrice = input.EntryPrice * (1m - distancePct);

        // Guard: for a long-only platform the stop must be below entry.
        if (stopPrice >= input.EntryPrice)
        {
            _logger.LogWarning(
                "BreakevenStop: computed fallback stop {StopPrice} is not below " +
                "entry {EntryPrice}. Clamping to entry price minus minimum distance (1%).",
                stopPrice, input.EntryPrice);
            stopPrice = input.EntryPrice * 0.99m;
        }

        return new StopLossResult(
            StopPrice: Math.Round(stopPrice, 2),
            StopTypeUsed: Name);
    }
}
