using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Portfolio Heat-based sizing model (portfolio-risk-guidelines § Position Sizing Models).
/// Sizes positions relative to current portfolio heat so that the proposed position
/// does not push total open risk above the configured maximum.
///
/// Portfolio Heat = Total Open Risk / Account Equity, expressed as a percentage.
///
/// Current portfolio heat must be provided via
/// <see cref="SizingInput.AdditionalInputs"/>["PortfolioHeat"] as a <c>decimal</c>
/// representing the percentage (e.g., 2.5m = 2.5%).
///
/// Formula:
///   availableRiskCapacity = equity × (MaxHeatPct - CurrentHeatPct) / 100
///   RecommendedQuantity = floor(availableRiskCapacity / stopDistance)
///   stopDistance = max(entryPrice - stopPrice, 0) or entryPrice if stopPrice is unavailable
///
/// Stateless and thread-safe; register as singleton.
/// </summary>
internal sealed class HeatBasedSizingModel : ISizingModel
{
    public string Name => "PortfolioHeat";

    private const string PortfolioHeatInputKey = "PortfolioHeat";

    private readonly RmeModuleOptions _options;
    private readonly ILogger<HeatBasedSizingModel> _logger;

    public HeatBasedSizingModel(
        IOptions<RmeModuleOptions> options,
        ILogger<HeatBasedSizingModel> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public SizingResult Calculate(SizingInput input)
    {
        var currentHeat = ExtractCurrentPortfolioHeat(input);
        var maxHeatPct = (decimal)_options.MaxPortfolioHeatPct;

        // ── Already at or above max heat ──────────────────────────────────
        if (currentHeat >= maxHeatPct)
        {
            _logger.LogWarning(
                "HeatBasedSizingModel: portfolio heat {CurrentHeat}% is at or above " +
                "maximum {MaxHeat}%. Returning zero quantity.",
                currentHeat, maxHeatPct);

            return new SizingResult(
                RecommendedQuantity: 0m,
                RiskAmount: 0m,
                ModelUsed: Name,
                AdvisoryMessage:
                    $"Portfolio heat ({currentHeat:F1}%) is at or above the maximum " +
                    $"({maxHeatPct:F1}%). No additional positions recommended.");
        }

        // ── Available risk capacity ──────────────────────────────────────
        var currentOpenRisk = input.AccountEquity * (currentHeat / 100m);
        var maxAllowedOpenRisk = input.AccountEquity * (maxHeatPct / 100m);
        var availableRiskCapacity = maxAllowedOpenRisk - currentOpenRisk;

        // ── Stop distance (per-share risk for the proposed position) ──────
        var stopDistance = DetermineStopDistance(input, out var advisoryMessage);

        if (stopDistance <= 0m)
        {
            _logger.LogWarning(
                "HeatBasedSizingModel: stop distance is zero or negative ({StopDistance}). " +
                "Returning zero quantity.",
                stopDistance);

            return new SizingResult(
                RecommendedQuantity: 0m,
                RiskAmount: availableRiskCapacity,
                ModelUsed: Name,
                AdvisoryMessage: "Cannot compute position size: stop distance is invalid.");
        }

        var recommendedQuantity = Math.Floor(availableRiskCapacity / stopDistance);

        _logger.LogInformation(
            "HeatBasedSizingModel: equity={Equity}, currentHeat={CurrentHeat}%, " +
            "maxHeat={MaxHeat}%, currentOpenRisk={CurrentRisk}, " +
            "maxAllowedOpenRisk={MaxRisk}, availableCapacity={AvailCap}, " +
            "stopDistance={StopDist}, qty={Qty}.",
            input.AccountEquity, currentHeat, maxHeatPct,
            currentOpenRisk, maxAllowedOpenRisk, availableRiskCapacity,
            stopDistance, recommendedQuantity);

        return new SizingResult(
            RecommendedQuantity: recommendedQuantity,
            RiskAmount: availableRiskCapacity,
            ModelUsed: Name,
            AdvisoryMessage: advisoryMessage);
    }

    private static decimal ExtractCurrentPortfolioHeat(SizingInput input)
    {
        if (input.AdditionalInputs is null)
            return 0m;

        if (!input.AdditionalInputs.TryGetValue(PortfolioHeatInputKey, out var value))
            return 0m;

        if (value is decimal d && d > 0m)
            return d;

        return 0m;
    }

    private static decimal DetermineStopDistance(SizingInput input, out string? advisory)
    {
        if (input.StopPrice > 0m && input.EntryPrice > input.StopPrice)
        {
            advisory = null;
            return input.EntryPrice - input.StopPrice;
        }

        if (input.EntryPrice > 0m)
        {
            advisory = "Stop price not provided; using entry price as risk proxy for heat calculation.";
            return input.EntryPrice;
        }

        advisory = "Invalid entry price.";
        return 0m;
    }
}
