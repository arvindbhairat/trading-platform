using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Notifications;

/// <summary>
/// MongoDB implementation of <see cref="INotificationRepository"/>.
/// Writes to the <c>notifications</c> collection.
/// </summary>
public sealed class MongoNotificationRepository : INotificationRepository
{
    private readonly IMongoCollection<NotificationDocument> _collection;

    public MongoNotificationRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<NotificationDocument>("notifications");
    }

    public async Task InsertAsync(
        NotificationDocument notification, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(notification, cancellationToken: ct);
    }

    public async Task InsertManyAsync(
        IEnumerable<NotificationDocument> notifications, CancellationToken ct = default)
    {
        await _collection.InsertManyAsync(notifications, cancellationToken: ct);
    }

    public async Task<List<NotificationDocument>> GetByUserIdAsync(
        ObjectId userId, int limit = 50, int skip = 0, CancellationToken ct = default)
    {
        var filter = Builders<NotificationDocument>.Filter.And(
            Builders<NotificationDocument>.Filter.Eq(n => n.UserId, userId),
            Builders<NotificationDocument>.Filter.Eq(n => n.IsAdminNotification, false));

        return await _collection
            .Find(filter)
            .SortByDescending(n => n.GeneratedAt)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<List<NotificationDocument>> GetAdminNotificationsAsync(
        int limit = 50, int skip = 0, CancellationToken ct = default)
    {
        var filter = Builders<NotificationDocument>.Filter
            .Eq(n => n.IsAdminNotification, true);

        return await _collection
            .Find(filter)
            .SortByDescending(n => n.GeneratedAt)
            .Skip(skip)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task MarkAsReadAsync(ObjectId id, CancellationToken ct = default)
    {
        var filter = Builders<NotificationDocument>.Filter.Eq(n => n.Id, id);
        var update = Builders<NotificationDocument>.Update.Set(n => n.IsRead, true);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task<long> CountUnreadByUserAsync(
        ObjectId userId, CancellationToken ct = default)
    {
        var filter = Builders<NotificationDocument>.Filter.And(
            Builders<NotificationDocument>.Filter.Eq(n => n.UserId, userId),
            Builders<NotificationDocument>.Filter.Eq(n => n.IsAdminNotification, false),
            Builders<NotificationDocument>.Filter.Eq(n => n.IsRead, false));

        return await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<long> CountUnreadAdminAsync(CancellationToken ct = default)
    {
        var filter = Builders<NotificationDocument>.Filter.And(
            Builders<NotificationDocument>.Filter.Eq(n => n.IsAdminNotification, true),
            Builders<NotificationDocument>.Filter.Eq(n => n.IsRead, false));

        return await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }
}
