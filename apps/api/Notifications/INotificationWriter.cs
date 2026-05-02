namespace SignalStack.Api.Notifications;

/// <summary>
/// High-level writer interface for creating notification records.
/// Used by all notification-producing components: EODSR, LMDS, RME.
/// REQ-NOTIFY-007: producers write to notifications collection only;
/// they do not dispatch to Telegram directly.
/// </summary>
public interface INotificationWriter
{
    /// <summary>Writes a single notification record to the collection.</summary>
    Task WriteAsync(NotificationDocument notification, CancellationToken ct = default);

    /// <summary>Writes a batch of notification records atomically.</summary>
    Task WriteBatchAsync(IEnumerable<NotificationDocument> notifications, CancellationToken ct = default);
}
