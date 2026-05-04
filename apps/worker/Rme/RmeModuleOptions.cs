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
    /// Drawdown threshold at which position-size reduction begins.
    /// When current drawdown exceeds this percentage, the drawdown-adjusted sizing
    /// model starts reducing the recommended position size proportionally, reaching
    /// zero at <see cref="DrawdownBlockPct"/>.
    /// Mapped from sys_config <c>risk.drawdown.threshold_reduce_size_pct</c> (REQ-DRDN-003).
    /// Default: 5.0 (5% drawdown from peak equity).
    /// </summary>
    [Range(0.0, 100.0)]
    public double DrawdownReductionStartPct { get; set; } = 5.0;

    /// <summary>
    /// Drawdown threshold at which new entry recommendations are blocked entirely.
    /// When current drawdown reaches or exceeds this percentage, the drawdown-adjusted
    /// sizing model returns zero quantity with an advisory message.
    /// Mapped from sys_config <c>risk.drawdown.threshold_block_new_entries_pct</c> (REQ-DRDN-003).
    /// Default: 20.0 (20% drawdown from peak equity).
    /// </summary>
    [Range(0.0, 100.0)]
    public double DrawdownBlockPct { get; set; } = 20.0;
}
