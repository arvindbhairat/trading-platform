namespace SignalStack.Signals.Backtesting;

/// <summary>
/// Strongly-typed RME profile configuration for a position.
/// Replaces the freeform BsonDocument / JSON approach.
/// REQ-RME-015: the profile is set at entry and governs the entire position lifecycle.
/// P6-T23: used by the profile optimisation engine as the grid evaluation unit.
/// </summary>
public sealed record RmeProfile
{
    /// <summary>Sizing model: "fixed_percentage", "atr", "heat_based", "drawdown_adjusted".</summary>
    public string SizingModelType { get; init; } = "fixed_percentage";

    /// <summary>Risk per trade as % of equity (for fixed_percentage sizing).</summary>
    public decimal? RiskPerTradePct { get; init; }

    /// <summary>ATR period for ATR-based sizing (e.g. 14).</summary>
    public int? AtrPeriod { get; init; }

    /// <summary>ATR multiplier for stop distance (e.g. 2.0 means 2× ATR from entry).</summary>
    public decimal? AtrStopMultiplier { get; init; }

    /// <summary>Maximum portfolio heat % (for heat_based sizing).</summary>
    public decimal? MaxHeatPct { get; init; }

    /// <summary>
    /// Stop loss type: "fixed", "atr", "trailing_percentage", "trailing_atr",
    /// "swing_low", "breakeven", "time_stop".
    /// </summary>
    public string StopLossType { get; init; } = "fixed";

    /// <summary>Fixed stop loss % below entry (for fixed stop).</summary>
    public decimal? StopLossPct { get; init; }

    /// <summary>Trailing stop type: "percentage", "atr" (when StopLossType is trailing).</summary>
    public string? TrailingStopType { get; init; }

    /// <summary>Trailing stop distance as % (for percentage trailing stop).</summary>
    public decimal? TrailingStopPct { get; init; }

    /// <summary>Primary evaluation timeframe.</summary>
    public string Timeframe { get; init; } = "daily";

    /// <summary>
    /// Optional secondary timeframe for multi-timeframe combinations.
    /// When set, the entry signal must agree on both timeframes.
    /// </summary>
    public string? SecondaryTimeframe { get; init; }

    /// <summary>Serialises to the same JSON shape as the legacy RmeConfiguration BsonDocument.</summary>
    public string ToJsonString()
    {
        var parts = new List<string>
        {
            $@"""position_sizing_model"":""{SizingModelType}""",
            $@"""stop_loss_type"":""{StopLossType}""",
            $@"""timeframe"":""{Timeframe}""",
        };

        if (RiskPerTradePct.HasValue)
            parts.Add($@"""risk_per_trade_pct"":{RiskPerTradePct.Value:F4}");
        if (AtrPeriod.HasValue)
            parts.Add($@"""atr_period"":{AtrPeriod.Value}");
        if (AtrStopMultiplier.HasValue)
            parts.Add($@"""atr_stop_multiplier"":{AtrStopMultiplier.Value:F2}");
        if (MaxHeatPct.HasValue)
            parts.Add($@"""max_heat_pct"":{MaxHeatPct.Value:F2}");
        if (StopLossPct.HasValue)
            parts.Add($@"""stop_loss_pct"":{StopLossPct.Value:F4}");
        if (TrailingStopType is not null)
            parts.Add($@"""trailing_stop_type"":""{TrailingStopType}""");
        if (TrailingStopPct.HasValue)
            parts.Add($@"""trailing_stop_pct"":{TrailingStopPct.Value:F4}");
        if (SecondaryTimeframe is not null)
            parts.Add($@"""secondary_timeframe"":""{SecondaryTimeframe}""");

        return "{ " + string.Join(", ", parts) + " }";
    }

    /// <summary>Human-readable label for this profile config.</summary>
    public string Label => $"{SizingModelType}/{StopLossType}/{Timeframe}" +
        (SecondaryTimeframe is not null ? $"+{SecondaryTimeframe}" : "");
}

/// <summary>
/// Developer-defined grid of profile configurations for optimisation.
/// REQ-RME-020: the grid defines the set of configurations to compare.
/// Users do not define the grid.
/// </summary>
public sealed record ProfileOptimisationGrid
{
    public string[] SizingModels { get; init; } = ["fixed_percentage", "atr"];
    public string[] StopLossTypes { get; init; } = ["fixed", "atr", "trailing_percentage"];
    public string[] Timeframes { get; init; } = ["daily"];
    public decimal[] RiskPctOptions { get; init; } = [0.5m, 1.0m];
    public decimal[] AtrMultiplierOptions { get; init; } = [1.5m, 2.0m, 2.5m];
    public string[][] MultiTimeframeCombos { get; init; } = [];

    /// <summary>
    /// Generates all profile configurations from the grid.
    /// </summary>
    public List<RmeProfile> GenerateAll()
    {
        var profiles = new List<RmeProfile>();

        foreach (var sizingModel in SizingModels)
        foreach (var stopLoss in StopLossTypes)
        foreach (var tf in Timeframes)
        {
            if (sizingModel == "fixed_percentage")
            {
                foreach (var riskPct in RiskPctOptions)
                {
                    profiles.Add(new RmeProfile
                    {
                        SizingModelType = sizingModel,
                        RiskPerTradePct = riskPct,
                        StopLossType = stopLoss,
                        StopLossPct = stopLoss == "fixed" ? riskPct : null,
                        AtrStopMultiplier = stopLoss == "atr" ? 2.0m : null,
                        Timeframe = tf,
                    });
                }
            }
            else if (sizingModel == "atr")
            {
                foreach (var atrMult in AtrMultiplierOptions)
                {
                    profiles.Add(new RmeProfile
                    {
                        SizingModelType = sizingModel,
                        AtrPeriod = 14,
                        AtrStopMultiplier = atrMult,
                        StopLossType = stopLoss,
                        StopLossPct = stopLoss == "fixed" ? 1.0m : null,
                        Timeframe = tf,
                    });
                }
            }
        }

        // Add multi-timeframe combinations (only for a representative subset
        // to keep the grid manageable)
        foreach (var combo in MultiTimeframeCombos)
        {
            if (combo.Length != 2) continue;

            foreach (var sizingModel in SizingModels)
            foreach (var stopLoss in StopLossTypes)
            {
                var riskPct = RiskPctOptions[0];
                profiles.Add(new RmeProfile
                {
                    SizingModelType = sizingModel,
                    RiskPerTradePct = riskPct,
                    StopLossType = stopLoss,
                    StopLossPct = stopLoss == "fixed" ? riskPct : null,
                    AtrStopMultiplier = stopLoss == "atr" ? 2.0m : null,
                    Timeframe = combo[0],
                    SecondaryTimeframe = combo[1],
                });
            }
        }

        return profiles;
    }
}
