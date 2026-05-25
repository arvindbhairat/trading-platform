namespace SignalStack.Domain.Notifications;

/// <summary>
/// Delivery status constants for the <c>notifications</c> collection.
/// REQ-NOTIFY-005: Telegram delivery tracking.
/// REQ-NOTIFY-021: Email delivery tracking for admin notifications.
/// </summary>
public static class DeliveryStatus
{
    // ── Telegram delivery status values ────────────────────────────────────
    public const string Pending = "pending";
    public const string Delivered = "delivered";
    public const string Failed = "failed";

    // ── Email delivery status values (admin notifications only) ────────────
    public const string EmailPending = "pending";
    public const string EmailDelivered = "delivered";
    public const string EmailFailed = "failed";
    public const string EmailNotAttempted = "not_attempted";
}
