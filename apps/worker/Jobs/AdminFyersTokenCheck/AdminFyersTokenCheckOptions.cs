using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Jobs.AdminFyersTokenCheck;

/// <summary>
/// Configuration options for the Admin FYERS Token Validity Check job.
///
/// REQ-NOTIFY-022:  daily post-market DataSync-window check (15:00 IST default).
/// REQ-NOTIFY-022b: pre-market check before market open (08:30 IST default).
/// </summary>
public sealed class AdminFyersTokenCheckOptions
{
    public const string SectionName = "AdminFyersTokenCheck";

    /// <summary>
    /// How often the worker polls to determine whether a scheduled check
    /// should be triggered. Default: 60 seconds — fine enough to catch
    /// the check-time boundary without excessive scanning.
    /// </summary>
    [Range(10, 300)]
    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Default pre-market check time (IST) when sys_config key
    /// <c>jobs.admin_token_check.pre_market_check_time</c> is not set.
    /// Format: "HH:mm" — default 08:30 IST.
    /// </summary>
    public string DefaultPreMarketCheckTime { get; init; } = "08:30";

    /// <summary>
    /// Default daily check time (IST) when sys_config key
    /// <c>jobs.admin_token_check.daily_check_time</c> is not set.
    /// Format: "HH:mm" — default 15:00 IST.
    /// </summary>
    public string DefaultDailyCheckTime { get; init; } = "15:00";

    /// <summary>
    /// Market session end time (IST) used to determine whether the admin
    /// token will remain valid through the full market session.
    /// REQ-NOTIFY-022b: token must be valid through 15:30 IST.
    /// </summary>
    public string MarketSessionEndTime { get; init; } = "15:30";
}
