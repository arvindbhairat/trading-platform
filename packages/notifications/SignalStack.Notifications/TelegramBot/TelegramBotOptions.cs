using System.ComponentModel.DataAnnotations;

namespace SignalStack.Notifications.TelegramBot;

/// <summary>
/// Configuration options for Telegram bot management.
/// </summary>
public sealed class TelegramBotOptions
{
    public const string SectionName = "TelegramBot";

    /// <summary>
    /// HTTP request timeout in seconds for Telegram getMe validation calls.
    /// Default: 10 seconds.
    /// </summary>
    [Range(5, 30)]
    public int ValidationTimeoutSeconds { get; init; } = 10;
}
