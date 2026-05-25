using SignalStack.Domain.Notifications;

namespace SignalStack.Storage.Notifications;

/// <summary>
/// Default implementation of <see cref="INotificationWriter"/>.
/// Validates notification types before persisting through the repository.
/// </summary>
public sealed class NotificationWriter : INotificationWriter
{
    private readonly INotificationRepository _repository;

    public NotificationWriter(INotificationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task WriteAsync(
        NotificationDocument notification, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        if (!NotificationType.IsValid(notification.NotificationType))
            throw new ArgumentException(
                $"Invalid notification type: '{notification.NotificationType}'. " +
                "Value must be one of the constants defined in NotificationType.",
                nameof(notification));

        await _repository.InsertAsync(notification, ct);
    }

    public async Task WriteBatchAsync(
        IEnumerable<NotificationDocument> notifications, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notifications);

        var list = notifications.ToList();

        foreach (var n in list)
        {
            if (!NotificationType.IsValid(n.NotificationType))
                throw new ArgumentException(
                    $"Invalid notification type: '{n.NotificationType}'. " +
                    "Value must be one of the constants defined in NotificationType.");
        }

        await _repository.InsertManyAsync(list, ct);
    }
}
