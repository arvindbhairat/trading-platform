using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Notifications;

/// <summary>
/// Notification record in the MongoDB <c>notifications</c> collection.
/// REQ-NOTIFY-005: schema covering all user-facing and admin notification payloads.
/// REQ-NOTIFY-006: user-facing notification types.
/// REQ-NOTIFY-006a: admin-only operational notification types.
/// REQ-NOTIFY-021: email delivery tracking for admin notifications.
/// REQ-NOTIFY-023: telegram_message_id for idempotent delivery.
/// </summary>
public sealed class NotificationDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Target user's MongoDB ObjectId.</summary>
    [BsonElement("user_id")]
    public required ObjectId UserId { get; init; }

    /// <summary>Notification type from <see cref="NotificationType"/> constants.</summary>
    [BsonElement("notification_type")]
    public required string NotificationType { get; init; }

    /// <summary>Signal type name when the notification relates to a Signal (e.g. "Price Volatility").</summary>
    [BsonElement("signal_type_name")]
    [BsonIgnoreIfNull]
    public string? SignalTypeName { get; init; }

    /// <summary>Timeframe used when the notification relates to a Signal evaluation.</summary>
    [BsonElement("timeframe")]
    [BsonIgnoreIfNull]
    public string? Timeframe { get; init; }

    /// <summary>Associated symbol when the notification relates to a specific symbol.</summary>
    [BsonElement("symbol")]
    [BsonIgnoreIfNull]
    public string? Symbol { get; init; }

    /// <summary>
    /// User-facing content text.
    /// Must comply with REQ-LEGAL-006 (self-directed language) and include the
    /// short-form disclaimer footer per REQ-LEGAL-007.
    /// </summary>
    [BsonElement("content")]
    public required string Content { get; init; }

    /// <summary>Deep-link URL for portal navigation (chart page, position page, etc.).</summary>
    [BsonElement("deep_link")]
    [BsonIgnoreIfNull]
    public string? DeepLink { get; init; }

    /// <summary>UTC timestamp when the notification was generated.</summary>
    [BsonElement("generated_at")]
    public required DateTime GeneratedAt { get; init; }

    // ── Telegram delivery fields (REQ-NOTIFY-005) ──────────────────────────

    /// <summary>MongoDB ObjectId of the Telegram bot used for delivery.</summary>
    [BsonElement("telegram_bot_id")]
    [BsonIgnoreIfNull]
    public ObjectId? TelegramBotId { get; set; }

    /// <summary>Telegram delivery status: pending, delivered, failed.</summary>
    [BsonElement("telegram_delivery_status")]
    public string TelegramDeliveryStatus { get; set; } = DeliveryStatus.Pending;

    /// <summary>Number of Telegram delivery attempts made.</summary>
    [BsonElement("telegram_delivery_attempts")]
    public int TelegramDeliveryAttempts { get; set; }

    /// <summary>Timestamp of the last Telegram delivery attempt.</summary>
    [BsonElement("last_telegram_attempt_at")]
    [BsonIgnoreIfNull]
    public DateTime? LastTelegramAttemptAt { get; set; }

    /// <summary>Telegram message_id returned by the bot API for idempotency (REQ-NOTIFY-023).</summary>
    [BsonElement("telegram_message_id")]
    [BsonIgnoreIfNull]
    public string? TelegramMessageId { get; set; }

    // ── Email delivery fields for admin notifications (REQ-NOTIFY-021) ─────

    /// <summary>Email delivery status: pending, delivered, failed, not_attempted.</summary>
    [BsonElement("email_delivery_status")]
    [BsonIgnoreIfNull]
    public string? EmailDeliveryStatus { get; set; }

    [BsonElement("email_attempts")]
    [BsonIgnoreIfNull]
    public int? EmailAttempts { get; set; }

    [BsonElement("last_email_attempt_at")]
    [BsonIgnoreIfNull]
    public DateTime? LastEmailAttemptAt { get; set; }

    // ── Admin routing (REQ-NOTIFY-006a) ────────────────────────────────────

    /// <summary>
    /// When true, this notification is admin-only and must not appear in
    /// regular user notification feeds.
    /// </summary>
    [BsonElement("is_admin_notification")]
    public bool IsAdminNotification { get; init; }

    // ── Portal feed state ──────────────────────────────────────────────────

    /// <summary>Whether the user has read this notification in the portal feed.</summary>
    [BsonElement("is_read")]
    public bool IsRead { get; set; }

    /// <summary>Error details from failed delivery attempts.</summary>
    [BsonElement("error_details")]
    [BsonIgnoreIfNull]
    public string? ErrorDetails { get; set; }
}
