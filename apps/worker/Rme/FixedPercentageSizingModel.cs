using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Fixed Percentage Risk Per Trade sizing model (REQ-SIZING-005).
/// Applies the configured default risk-per-trade percentage to the account equity
/// to derive a maximum risk amount, then computes the recommended quantity by
/// dividing by the entry price.
///
/// This is the default sizing model and is always available.
/// Band bounds are configured in sys_config under the <c>position_sizing</c> category
/// and are enforced at the RME module level — this model reads the default value
/// from <see cref="RmeModuleOptions.DefaultRiskPerTradePct"/>.
/// </summary>
internal sealed class FixedPercentageSizingModel : ISizingModel
{
    public string Name => "FixedPercentage";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<FixedPercentageSizingModel> _logger;

    public FixedPercentageSizingModel(
        IOptions<RmeModuleOptions> options,
        ILogger<FixedPercentageSizingModel> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SizingResult Calculate(SizingInput input)
    {
        var riskPct = (decimal)_options.DefaultRiskPerTradePct / 100m;
        var riskAmount = input.AccountEquity * riskPct;

        var recommendedQuantity = input.EntryPrice > 0
            ? Math.Floor(riskAmount / input.EntryPrice)
            : 0m;

        _logger.LogInformation(
            "FixedPercentageSizingModel: equity={Equity}, riskPct={RiskPct}% = riskAmount={RiskAmount}, " +
            "qty={Qty} @ price={Price}.",
            input.AccountEquity, _options.DefaultRiskPerTradePct,
            riskAmount, recommendedQuantity, input.EntryPrice);

        return new SizingResult(
            RecommendedQuantity: recommendedQuantity,
            RiskAmount: riskAmount,
            ModelUsed: Name,
            AdvisoryMessage: null);
    }
}
