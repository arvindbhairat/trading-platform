using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Top-level RME module configuration. Seeded in <c>sys_config</c> under the
/// <c>rme</c> category; defaults are compiled-in fallbacks only.
/// REQ-RME-001, REQ-RME-012.
/// </summary>
public sealed class RmeModuleOptions
{
    public const string SectionName = "Worker:Rme:Module";

    /// <summary>
    /// Maximum number of concurrently active (non-terminal) positions the RME
    /// will track. Used as a circuit-breaker threshold for the channel registry.
    /// Default: 200 (Phase A ceiling per RME-L1).
    /// </summary>
    [Range(1, 10_000)]
    public int MaxActivePositions { get; set; } = 200;

    /// <summary>
    /// Default risk-per-trade as a percentage of account equity.
    /// Applied when the user has not configured an explicit value for their
    /// Signal Subscription. REQ-SIZING-005.
    /// Default: 0.5 (0.5%).
    /// </summary>
    [Range(0.1, 100.0)]
    public double DefaultRiskPerTradePct { get; set; } = 0.5;

    /// <summary>
    /// Minimum allowed risk-per-trade percentage (sys_config band floor).
    /// REQ-SIZING-005.
    /// Default: 0.5 (0.5%).
    /// </summary>
    [Range(0.1, 100.0)]
    public double MinRiskPerTradePct { get; set; } = 0.5;

    /// <summary>
    /// Maximum allowed risk-per-trade percentage (sys_config band ceiling).
    /// REQ-SIZING-005.
    /// Default: 1.0 (1.0%).
    /// </summary>
    [Range(0.1, 100.0)]
    public double MaxRiskPerTradePct { get; set; } = 1.0;

    /// <summary>
    /// ATR multiplier for ATR-based sizing (portfolio-risk-guidelines § Position Sizing Models).
    /// The stop distance used for position sizing is ATR × AtrMultiplier.
    /// Default: 2.0 (stop at 2× ATR from entry).
    /// </summary>
    [Range(0.5, 10.0)]
    public double AtrMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum portfolio heat as a percentage of account equity.
    /// Portfolio Heat = Total Open Risk / Account Equity, expressed as a percentage.
    /// The heat-based sizing model ensures that adding a new position does not push
    /// total portfolio heat above this threshold.
    /// Mapped from sys_config <c>risk.default.max_portfolio_heat_pct</c> (REQ-HEAT-002).
    /// Default: 5.0 (5% of equity).
    /// </summary>
    [Range(1.0, 100.0)]
    public double MaxPortfolioHeatPct { get; set; } = 5.0;

    /// <summary>
    /// Default fixed stop distance expressed as a percentage below the entry price.
    /// Applied when no explicit <c>FixedStopPrice</c> is provided in the stop-loss
    /// input's additional parameters.
    /// (portfolio-risk-guidelines § Stop Loss Types — Fixed Stop Loss).
    /// Default: 5.0 (5% below entry price).
    /// </summary>
    [Range(0.1, 50.0)]
    public double DefaultFixedStopDistancePct { get; set; } = 5.0;

    /// <summary>
    /// Default trailing stop percentage. The stop trails at this percentage
    /// below the highest price observed since entry.
    /// Default: 10.0 (10% below the highest price since entry).
    /// </summary>
    [Range(0.1, 50.0)]
    public double DefaultTrailingStopPercent { get; set; } = 10.0;

    /// <summary>
    /// Default buffer percentage applied below the swing low price.
    /// The Swing Low Stop places the stop at <c>SwingLowPrice × (1 - bufferPct/100)</c>
    /// to ensure the stop is strictly below the identified swing low, reducing
    /// the likelihood of premature stop-outs on swing-low retests.
    /// Mapped from sys_config <c>risk.stop.swing_low_buffer_pct</c>.
    /// Default: 0.0 (stop placed exactly at the swing low price).
    /// </summary>
    [Range(0.0, 10.0)]
    public double DefaultSwingLowBufferPct { get; set; } = 0.0;

    /// <summary>
    /// R-multiple threshold for breakeven stop activation.
    /// When the position's current R-multiple reaches or exceeds this value,
    /// the <see cref="BreakevenStopLoss"/> moves the stop to the entry price.
    /// Mapped from sys_config <c>risk.stop.breakeven_trigger_r</c> (REQ-STOP-009).
    /// Default: 1.0 (1R gain triggers breakeven).
    /// </summary>
    [Range(0.1, 10.0)]
    public double DefaultBreakevenTriggerR { get; set; } = 1.0;

    /// <summary>
    /// Drawdown threshold at which position-size reduction begins.
    /// When current drawdown exceeds this percentage, the drawdown-adjusted sizing
    /// model starts reducing the recommended position size proportionally, reaching
    /// zero at <see cref="DrawdownBlockPct"/>.
    /// Mapped from sys_config <c>risk.drawdown_enforcement.size_reduction_pct</c> (REQ-DRDN-003,
    /// REQ-DRDN-004 naming convention). Compiled-in fallback; runtime value from <see cref="IDrawdownTracker"/>.
    /// Default: 5.0 (5% drawdown from peak equity).
    /// </summary>
    [Range(0.0, 100.0)]
    public double DrawdownReductionStartPct { get; set; } = 5.0;

    /// <summary>
    /// Drawdown threshold at which new entry recommendations are blocked entirely.
    /// When current drawdown reaches or exceeds this percentage, the drawdown-adjusted
    /// sizing model returns zero quantity with an advisory message.
    /// Mapped from sys_config <c>risk.drawdown_enforcement.suppress_entry_pct</c> (REQ-DRDN-003,
    /// REQ-DRDN-004 naming convention). Compiled-in fallback; runtime value from <see cref="IDrawdownTracker"/>.
    /// Default: 20.0 (20% drawdown from peak equity).
    /// </summary>
    [Range(0.0, 100.0)]
    public double DrawdownBlockPct { get; set; } = 20.0;
}
