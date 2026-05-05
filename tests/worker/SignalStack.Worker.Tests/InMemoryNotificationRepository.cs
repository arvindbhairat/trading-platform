using System.Collections.Concurrent;
using MongoDB.Bson;
using SignalStack.Api.Notifications;

namespace SignalStack.Worker.Tests;

/// <summary>
/// In-memory <see cref="INotificationRepository"/> for worker tests without a live MongoDB connection.
/// </summary>
public sealed class InMemoryNotificationRepository : INotificationRepository
{
    private readonly ConcurrentDictionary<ObjectId, NotificationDocument> _store = new();
    private readonly ConcurrentBag<NotificationDocument> _insertionOrder = new();

    public Task InsertAsync(NotificationDocument notification, CancellationToken ct = default)
    {
        var doc = Clone(notification);
        _store.TryAdd(doc.Id, doc);
        _insertionOrder.Add(doc);
        return Task.CompletedTask;
    }

    public Task InsertManyAsync(IEnumerable<NotificationDocument> notifications, CancellationToken ct = default)
    {
        foreach (var n in notifications)
        {
            var doc = Clone(n);
            _store.TryAdd(doc.Id, doc);
            _insertionOrder.Add(doc);
        }
        return Task.CompletedTask;
    }

    public Task<List<NotificationDocument>> GetByUserIdAsync(
        ObjectId userId, int limit = 50, int skip = 0, CancellationToken ct = default)
    {
        var results = _insertionOrder
            .Where(n => n.UserId == userId && !n.IsAdminNotification)
            .OrderByDescending(n => n.GeneratedAt)
            .Skip(skip)
            .Take(limit)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<List<NotificationDocument>> GetAdminNotificationsAsync(
        int limit = 50, int skip = 0, CancellationToken ct = default)
    {
        var results = _insertionOrder
            .Where(n => n.IsAdminNotification)
            .OrderByDescending(n => n.GeneratedAt)
            .Skip(skip)
            .Take(limit)
            .ToList();
        return Task.FromResult(results);
    }

    public Task MarkAsReadAsync(ObjectId id, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var doc))
        {
            doc.IsRead = true;
        }
        return Task.CompletedTask;
    }

    public Task<long> CountUnreadByUserAsync(ObjectId userId, CancellationToken ct = default)
    {
        var count = _insertionOrder
            .Count(n => n.UserId == userId && !n.IsAdminNotification && !n.IsRead);
        return Task.FromResult((long)count);
    }

    public Task<long> CountUnreadAdminAsync(CancellationToken ct = default)
    {
        var count = _insertionOrder
            .Count(n => n.IsAdminNotification && !n.IsRead);
        return Task.FromResult((long)count);
    }

    public Task<long> CountUnreadCriticalByUserAsync(
        ObjectId userId, CancellationToken ct = default)
    {
        var count = _insertionOrder
            .Count(n => n.UserId == userId && !n.IsAdminNotification && !n.IsRead
                        && NotificationType.Critical.Contains(n.NotificationType));
        return Task.FromResult((long)count);
    }

    public Task<List<NotificationDocument>> GetByUserIdFilteredAsync(
        ObjectId userId,
        int limit = 50,
        int skip = 0,
        string? notificationType = null,
        string? symbol = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null,
        string? deliveryStatus = null,
        CancellationToken ct = default)
    {
        var query = _insertionOrder
            .Where(n => n.UserId == userId && !n.IsAdminNotification);

        if (notificationType is not null)
            query = query.Where(n => n.NotificationType == notificationType);
        if (symbol is not null)
            query = query.Where(n => n.Symbol == symbol);
        if (dateFrom is not null)
            query = query.Where(n => n.GeneratedAt >= dateFrom.Value);
        if (dateTo is not null)
            query = query.Where(n => n.GeneratedAt <= dateTo.Value);
        if (deliveryStatus is not null)
            query = query.Where(n => n.TelegramDeliveryStatus == deliveryStatus);

        var results = query
            .OrderByDescending(n => n.GeneratedAt)
            .Skip(skip)
            .Take(limit)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<long> MarkAllAsReadAsync(ObjectId userId, CancellationToken ct = default)
    {
        long count = 0;
        foreach (var kvp in _store)
        {
            if (kvp.Value.UserId == userId && !kvp.Value.IsRead)
            {
                kvp.Value.IsRead = true;
                count++;
            }
        }
        return Task.FromResult(count);
    }

    public List<NotificationDocument> GetAll() => _insertionOrder.ToList();

    public void Clear()
    {
        _store.Clear();
        _insertionOrder.Clear();
    }

    private static NotificationDocument Clone(NotificationDocument source)
    {
        return new NotificationDocument
        {
            Id = source.Id,
            UserId = source.UserId,
            NotificationType = source.NotificationType,
            SignalTypeName = source.SignalTypeName,
            Timeframe = source.Timeframe,
            Symbol = source.Symbol,
            Content = source.Content,
            DeepLink = source.DeepLink,
            GeneratedAt = source.GeneratedAt,
            TelegramBotId = source.TelegramBotId,
            TelegramDeliveryStatus = source.TelegramDeliveryStatus,
            TelegramDeliveryAttempts = source.TelegramDeliveryAttempts,
            LastTelegramAttemptAt = source.LastTelegramAttemptAt,
            TelegramMessageId = source.TelegramMessageId,
            EmailDeliveryStatus = source.EmailDeliveryStatus,
            EmailAttempts = source.EmailAttempts,
            LastEmailAttemptAt = source.LastEmailAttemptAt,
            IsAdminNotification = source.IsAdminNotification,
            IsRead = source.IsRead,
            ErrorDetails = source.ErrorDetails,
        };
    }
}
