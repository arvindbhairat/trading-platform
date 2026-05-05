using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Jobs.SloBreachMonitor;

/// <summary>
/// Configuration options for SLO breach notification routing.
///
/// REQ-SLO-008: cooldown of <see cref="BreachNotificationCooldownMinutes"/>
/// applies per SLO per session to prevent alert fatigue.
/// </summary>
public sealed class SloBreachMonitorOptions
{
    public const string SectionName = "SloBreachMonitor";

    /// <summary>
    /// Cooldown period in minutes between SLO breach notifications for the
    /// same SLO. Default: 60 minutes. Config key: <c>slos.breach_notification_cooldown_minutes</c>.
    /// </summary>
    [Range(1, 1440)]
    public int BreachNotificationCooldownMinutes { get; init; } = 60;
}
