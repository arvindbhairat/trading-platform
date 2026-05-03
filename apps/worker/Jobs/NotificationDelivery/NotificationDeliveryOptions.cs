using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Jobs.NotificationDelivery;

/// <summary>
/// Configuration options for the Notification Delivery Job (NDJ).
///
/// REQ-NOTIFY-008: Telegram delivery with retry and rate-limit awareness.
/// REQ-NOTIFY-019: dirty-link auto-disable threshold.
/// REQ-NOTIFY-022a: global-disable break-glass flag.
/// </summary>
public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "NotificationDelivery";

    /// <summary>
    /// How often the NDJ worker polls for pending notifications.
    /// Default: 10 seconds (consistent with REQ-SLO-005 p95 ≤ 30 s).
    /// </summary>
    [Range(1, 300)]
    public int PollIntervalSeconds { get; init; } = 10;

    /// <summary>
    /// Maximum number of pending notifications to fetch per poll cycle.
    /// Default: 50 messages per cycle.
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; init; } = 50;

    /// <summary>
    /// Telegram Bot API base URL template.
    /// "{BotToken}" is replaced with the actual bot token at runtime.
    /// Default: "https://api.telegram.org/bot{BotToken}/".
    /// </summary>
    public string TelegramApiBaseUrlTemplate { get; init; } = "https://api.telegram.org/bot{BotToken}/";

    /// <summary>
    /// HTTP request timeout in seconds for Telegram API calls.
    /// Default: 15 seconds.
    /// </summary>
    [Range(5, 120)]
    public int TelegramApiTimeoutSeconds { get; init; } = 15;
}
