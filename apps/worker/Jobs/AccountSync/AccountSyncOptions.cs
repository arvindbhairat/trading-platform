using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Jobs.AccountSync;

/// <summary>
/// Configuration options for the Live Account Data Sync (LADS) job.
///
/// REQ-PORT-019: intraday recurring interval during market hours.
/// REQ-PORT-021a: cycle-level abort tracking + graduated suspension thresholds.
/// </summary>
public sealed class AccountSyncOptions
{
    public const string SectionName = "AccountSync";

    /// <summary>
    /// How often the LADS worker polls the trading calendar and sys_config
    /// to determine whether a new intraday sync cycle should begin.
    /// Default: 60 seconds — fast enough to react to market-open and
    /// interval changes without excessive collection scanning.
    /// </summary>
    [Range(10, 300)]
    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Default intra-interval for per-user sync cycles when the sys_config
    /// key <c>jobs.account_sync.intraday_interval_minutes</c> is not set.
    /// Must be one of: 5, 10, 15, 30, 60.
    /// REQ-PORT-019: interval stored in sys_config.
    /// </summary>
    [Range(1, 60)]
    public int DefaultIntradayIntervalMinutes { get; init; } = 15;

    /// <summary>
    /// Maximum users to process per poll cycle.
    /// With the Phase A tester ceiling of 30, batching is mainly relevant
    /// for future scaling. Default: 50.
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Consecutive cycle-abort count that triggers an admin System Health
    /// widget warning (REQ-PORT-021a(b)). Default: 2.
    /// </summary>
    [Range(1, 10)]
    public int ConsecutiveAbortWarnThreshold { get; init; } = 2;

    /// <summary>
    /// Consecutive cycle-abort count that triggers platform-wide suspension,
    /// admin Telegram alert, and an <c>rme_incidents</c> record
    /// (REQ-PORT-021a(c)). Must be strictly greater than
    /// <see cref="ConsecutiveAbortWarnThreshold"/>. Default: 3.
    /// </summary>
    [Range(2, 10)]
    public int ConsecutiveAbortSuspendThreshold { get; init; } = 3;
}
