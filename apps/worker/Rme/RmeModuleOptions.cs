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
}
