using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalStack.Worker.Rme;

/// <summary>
/// ATR and Volatility-Based Sizing model (portfolio-risk-guidelines § Position Sizing Models).
/// Position size is adjusted for volatility so that positions in more volatile instruments
/// are sized smaller proportionally.
///
/// Formula:
///   riskAmount = AccountEquity × (DefaultRiskPerTradePct / 100)
///   stopDistance = ATR × AtrMultiplier
///   RecommendedQuantity = floor(riskAmount / stopDistance)
///
/// ATR value must be provided via <see cref="SizingInput.AdditionalInputs"/>["ATR"] as a <c>decimal</c>.
/// Stateless and thread-safe; register as singleton.
/// </summary>
internal sealed class AtrSizingModel : ISizingModel
{
    public string Name => "AtrBased";

    private const string AtrInputKey = "ATR";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<AtrSizingModel> _logger;

    public AtrSizingModel(
        IOptions<RmeModuleOptions> options,
        ILogger<AtrSizingModel> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SizingResult Calculate(SizingInput input)
    {
        var atrValue = ExtractAtr(input);

        if (atrValue <= 0m)
        {
            var riskAmount = input.AccountEquity * ((decimal)_options.DefaultRiskPerTradePct / 100m);

            _logger.LogWarning(
                "AtrSizingModel: ATR value is missing, zero, or invalid. " +
                "Returning zero quantity. Provided AdditionalInputs keys: {Keys}",
                input.AdditionalInputs?.Keys is not null
                    ? string.Join(", ", input.AdditionalInputs.Keys)
                    : "(none)");

            return new SizingResult(
                RecommendedQuantity: 0m,
                RiskAmount: riskAmount,
                ModelUsed: Name,
                AdvisoryMessage: "ATR value is required for ATR-based sizing but was missing or zero.");
        }

        var atrMultiplier = (decimal)_options.AtrMultiplier;
        var riskPct = (decimal)_options.DefaultRiskPerTradePct / 100m;
        var riskAmountCalc = input.AccountEquity * riskPct;
        var stopDistance = atrValue * atrMultiplier;
        var recommendedQuantity = stopDistance > 0
            ? Math.Floor(riskAmountCalc / stopDistance)
            : 0m;

        _logger.LogInformation(
            "AtrSizingModel: equity={Equity}, riskPct={RiskPct}%, ATR={Atr}, " +
            "multiplier={Multiplier}, stopDistance={StopDist}, riskAmount={RiskAmount}, " +
            "qty={Qty}.",
            input.AccountEquity, _options.DefaultRiskPerTradePct,
            atrValue, atrMultiplier, stopDistance, riskAmountCalc,
            recommendedQuantity);

        return new SizingResult(
            RecommendedQuantity: recommendedQuantity,
            RiskAmount: riskAmountCalc,
            ModelUsed: Name,
            AdvisoryMessage: null);
    }

    private static decimal ExtractAtr(SizingInput input)
    {
        if (input.AdditionalInputs is null)
            return 0m;

        if (!input.AdditionalInputs.TryGetValue(AtrInputKey, out var atrObj))
            return 0m;

        return atrObj is decimal atrDecimal ? atrDecimal : 0m;
    }
}
