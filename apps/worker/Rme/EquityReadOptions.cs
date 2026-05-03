using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Configuration for the snapshot-consistent equity base read protocol (REQ-RME-006d).
/// Bound to <c>Worker:Rme:EquityRead</c> config section with compiled-in
/// fallbacks that mirror the <c>sys_config</c> defaults.
/// </summary>
public sealed class EquityReadOptions
{
    public const string SectionName = "Worker:Rme:EquityRead";

    /// <summary>
    /// Maximum retry attempts for the V1/V2 snapshot-consistency check.
    /// Default: 3 (matches <c>rme.equity_read.snapshot_retry_max_attempts</c>).
    /// </summary>
    [Range(0, 20)]
    public int SnapshotRetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Back-off in milliseconds between snapshot retries.
    /// Default: 50 ms (matches <c>rme.equity_read.snapshot_retry_backoff_ms</c>).
    /// </summary>
    [Range(10, 5000)]
    public int SnapshotRetryBackoffMs { get; set; } = 50;
}
