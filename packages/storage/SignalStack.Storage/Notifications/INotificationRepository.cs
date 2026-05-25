using MongoDB.Bson;
using SignalStack.Domain.Notifications;

namespace SignalStack.Storage.Notifications;

/// <summary>
/// Repository for the <c>notifications</c> MongoDB collection.
/// REQ-NOTIFY-004: synchronous persistence before Telegram delivery.
/// </summary>
public interface INotificationRepository
{
    /// <summary>Inserts a single notification record.</summary>
    Task InsertAsync(NotificationDocument notification, CancellationToken ct = default);

    /// <summary>Inserts multiple notification records in a single batch.</summary>
    Task InsertManyAsync(IEnumerable<NotificationDocument> notifications, CancellationToken ct = default);

    /// <summary>Returns notifications for a user, ordered by generated_at descending.</summary>
    Task<List<NotificationDocument>> GetByUserIdAsync(
        ObjectId userId, int limit = 50, int skip = 0, CancellationToken ct = default);

    /// <summary>Returns admin-only notifications, ordered by generated_at descending.</summary>
    Task<List<NotificationDocument>> GetAdminNotificationsAsync(
        int limit = 50, int skip = 0, CancellationToken ct = default);

    /// <summary>Marks a notification as read.</summary>
    Task MarkAsReadAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Returns the total count of unread notifications for a user.</summary>
    Task<long> CountUnreadByUserAsync(ObjectId userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of unread critical (non-suppressible) notifications for a user.
    /// Non-suppressible types per REQ-PROFILE-006: exit_alert, gap_risk_alert,
    /// circuit_limit_alert, market_halt_active, market_halt_cleared, pending_entry_superseded.
    /// </summary>
    Task<long> CountUnreadCriticalByUserAsync(ObjectId userId, CancellationToken ct = default);

    /// <summary>Returns the count of unread admin notifications.</summary>
    Task<long> CountUnreadAdminAsync(CancellationToken ct = default);

    /// <summary>Returns notifications for a user with optional filters, ordered by generated_at descending.</summary>
    Task<List<NotificationDocument>> GetByUserIdFilteredAsync(
        ObjectId userId,
        int limit = 50,
        int skip = 0,
        string? notificationType = null,
        string? symbol = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? deliveryStatus = null,
        CancellationToken ct = default);

    /// <summary>Marks all unread notifications for a user as read. Returns the count modified.</summary>
    Task<long> MarkAllAsReadAsync(ObjectId userId, CancellationToken ct = default);
}
