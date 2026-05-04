using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Drawdown-adjusted sizing model (portfolio-risk-guidelines § Position Sizing Models).
/// Applies a graduated reduction to the base risk amount when the account drawdown
/// exceeds the configured start threshold, reducing position size proportionally
/// as drawdown deepens. When drawdown reaches or exceeds the block threshold,
/// new-entry recommendations are blocked entirely.
///
/// Current drawdown must be provided via
/// <see cref="SizingInput.AdditionalInputs"/>["CurrentDrawdownPct"] as a <c>decimal</c>
/// representing the peak-to-trough drawdown as a percentage (e.g., 8.5m = 8.5%).
///
/// Formula:
///   baseRiskAmount = AccountEquity × (DefaultRiskPerTradePct / 100)
///   if drawdown ≤ ReductionStartPct:     factor = 1.0 (no reduction)
///   if drawdown ≥ BlockPct:              factor = 0.0 (blocked, return zero)
///   else:                                factor = 1.0 − (drawdown − ReductionStartPct)
///                                                   / (BlockPct − ReductionStartPct)
///   adjustedRiskAmount = baseRiskAmount × factor
///   RecommendedQuantity = floor(adjustedRiskAmount / stopDistance)
///
/// Stateless and thread-safe; register as singleton.
/// </summary>
internal sealed class DrawdownAdjustedSizingModel : ISizingModel
{
    public string Name => "DrawdownAdjusted";

    private const string DrawdownInputKey = "CurrentDrawdownPct";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<DrawdownAdjustedSizingModel> _logger;

    public DrawdownAdjustedSizingModel(
        IOptions<RmeModuleOptions> options,
        ILogger<DrawdownAdjustedSizingModel> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SizingResult Calculate(SizingInput input)
    {
        var currentDrawdownPct = ExtractCurrentDrawdown(input);

        // ── Drawdown at or above block threshold → zero quantity ──────────
        if (currentDrawdownPct >= (decimal)_options.DrawdownBlockPct)
        {
            _logger.LogWarning(
                "DrawdownAdjustedSizingModel: drawdown {DrawdownPct:F1}% is at or above " +
                "block threshold {BlockPct}%. Returning zero quantity.",
                currentDrawdownPct, _options.DrawdownBlockPct);

            return new SizingResult(
                RecommendedQuantity: 0m,
                RiskAmount: 0m,
                ModelUsed: Name,
                AdvisoryMessage:
                    $"Drawdown ({currentDrawdownPct:F1}%) is at or above the block " +
                    $"threshold ({_options.DrawdownBlockPct:F1}%). No new entries recommended.");
        }

        // ── Compute base risk amount ──────────────────────────────────────
        var riskPct = (decimal)_options.DefaultRiskPerTradePct / 100m;
        var baseRiskAmount = input.AccountEquity * riskPct;

        // ── Compute reduction factor ──────────────────────────────────────
        var reductionStart = (decimal)_options.DrawdownReductionStartPct;
        var blockPct = (decimal)_options.DrawdownBlockPct;

        decimal reductionFactor;
        string? advisoryMessage = null;

        if (currentDrawdownPct <= reductionStart)
        {
            reductionFactor = 1.0m;
        }
        else
        {
            var range = blockPct - reductionStart;
            var excess = currentDrawdownPct - reductionStart;
            reductionFactor = range > 0m
                ? Math.Max(0m, 1.0m - (excess / range))
                : 0m;

            advisoryMessage =
                $"Drawdown of {currentDrawdownPct:F1}% is above the reduction start " +
                $"threshold ({reductionStart:F1}%). Position size reduced by factor " +
                $"{reductionFactor:F2}.";
        }

        var adjustedRiskAmount = baseRiskAmount * reductionFactor;

        // ── Stop distance ─────────────────────────────────────────────────
        var stopDistance = DetermineStopDistance(input);

        if (stopDistance <= 0m)
        {
            _logger.LogWarning(
                "DrawdownAdjustedSizingModel: stop distance is zero or negative ({StopDistance}). " +
                "Returning zero quantity.",
                stopDistance);

            return new SizingResult(
                RecommendedQuantity: 0m,
                RiskAmount: adjustedRiskAmount,
                ModelUsed: Name,
                AdvisoryMessage: "Cannot compute position size: stop distance is invalid.");
        }

        var recommendedQuantity = Math.Floor(adjustedRiskAmount / stopDistance);

        _logger.LogInformation(
            "DrawdownAdjustedSizingModel: equity={Equity}, riskPct={RiskPct}%, " +
            "drawdown={DrawdownPct}%, reductionStart={ReductionStart}%, " +
            "blockPct={BlockPct}%, reductionFactor={Factor}, baseRisk={BaseRisk}, " +
            "adjustedRisk={AdjRisk}, stopDistance={StopDist}, qty={Qty}.",
            input.AccountEquity, _options.DefaultRiskPerTradePct,
            currentDrawdownPct, _options.DrawdownReductionStartPct,
            _options.DrawdownBlockPct, reductionFactor,
            baseRiskAmount, adjustedRiskAmount, stopDistance, recommendedQuantity);

        return new SizingResult(
            RecommendedQuantity: recommendedQuantity,
            RiskAmount: adjustedRiskAmount,
            ModelUsed: Name,
            AdvisoryMessage: advisoryMessage);
    }

    private static decimal ExtractCurrentDrawdown(SizingInput input)
    {
        if (input.AdditionalInputs is null)
            return 0m;

        if (!input.AdditionalInputs.TryGetValue(DrawdownInputKey, out var value))
            return 0m;

        return value is decimal d && d >= 0m ? d : 0m;
    }

    private static decimal DetermineStopDistance(SizingInput input)
    {
        if (input.StopPrice > 0m && input.EntryPrice > input.StopPrice)
            return input.EntryPrice - input.StopPrice;

        if (input.EntryPrice > 0m)
            return input.EntryPrice;

        return 0m;
    }
}
