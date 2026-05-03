using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Jobs.LiveMarketScan;

/// <summary>
/// Configuration options for the Live Market Data Scan (LMDS) job.
///
/// REQ-STOP-006:  continuous intraday price + level monitoring during market hours.
/// REQ-SLO-004:   scan cycle must complete within 70 % of poll interval.
/// REQ-SLO-011:   capacity warning at <see cref="CapacityWarnPct"/>.
/// </summary>
public sealed class LiveMarketScanOptions
{
    public const string SectionName = "LiveMarketScan";

    /// <summary>
    /// How often the LMDS worker polls for a new scan cycle.
    /// Default: 90 seconds per REQ-STOP-006.
    /// </summary>
    [Range(10, 600)]
    public int PollIntervalSeconds { get; init; } = 90;

    /// <summary>
    /// Fraction (0–100) of the poll interval that triggers a capacity warning
    /// when exceeded by the scan cycle duration. Default: 80.
    /// REQ-SLO-011, sys_config key <c>jobs.lmds.capacity_warn_pct</c>.
    /// </summary>
    [Range(1, 100)]
    public int CapacityWarnPct { get; init; } = 80;

    /// <summary>
    /// Maximum number of positions whose quotes to fetch per cycle.
    /// Default: 500 — well above the expected Phase A ceiling.
    /// </summary>
    [Range(1, 5000)]
    public int MaxPositionsPerCycle { get; init; } = 500;

    /// <summary>
    /// Number of consecutive cycles with zero valid quotes that triggers
    /// a stale-market circuit warning. Default: 3.
    /// REQ-PLC-004: circuit-clear re-evaluation.
    /// </summary>
    [Range(1, 20)]
    public int StaleQuoteCircuitThreshold { get; init; } = 3;
}
