using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Signals;

public sealed class MongoSignalSubscriptionRepository : ISignalSubscriptionRepository
{
    private readonly IMongoCollection<SignalSubscriptionDocument> _collection;

    public MongoSignalSubscriptionRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<SignalSubscriptionDocument>("signals");
    }

    public async Task<List<SignalSubscriptionDocument>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        return await _collection
            .Find(s => s.UserId == userId)
            .Sort(Builders<SignalSubscriptionDocument>.Sort.Descending(s => s.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<SignalSubscriptionDocument?> GetByIdAsync(
        ObjectId id, CancellationToken ct = default)
    {
        return await _collection
            .Find(s => s.Id == id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SignalSubscriptionDocument?> GetByIdAndUserAsync(
        ObjectId id, string userId, CancellationToken ct = default)
    {
        return await _collection
            .Find(s => s.Id == id && s.UserId == userId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task CreateAsync(
        SignalSubscriptionDocument subscription, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(subscription, cancellationToken: ct);
    }

    public async Task ReplaceAsync(
        SignalSubscriptionDocument subscription, CancellationToken ct = default)
    {
        await _collection.ReplaceOneAsync(
            s => s.Id == subscription.Id,
            subscription,
            cancellationToken: ct);
    }

    public async Task UpdateStatusAsync(
        ObjectId id, string newStatus, DateTime updatedAt, CancellationToken ct = default)
    {
        var filter = Builders<SignalSubscriptionDocument>.Filter.Eq(s => s.Id, id);
        var update = Builders<SignalSubscriptionDocument>.Update
            .Set(s => s.Status, newStatus)
            .Set(s => s.UpdatedAt, updatedAt);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task AddVersionAsync(
        ObjectId id,
        RmeConfigurationVersion newVersion,
        int newPendingIndex,
        DateTime updatedAt,
        CancellationToken ct = default)
    {
        var filter = Builders<SignalSubscriptionDocument>.Filter.Eq(s => s.Id, id);
        var update = Builders<SignalSubscriptionDocument>.Update
            .Push(s => s.Versions, newVersion)
            .Set(s => s.PendingVersionIndex, newPendingIndex)
            .Set(s => s.UpdatedAt, updatedAt);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task DiscardPendingVersionAsync(
        ObjectId id, DateTime updatedAt, CancellationToken ct = default)
    {
        // Find the subscription to identify which version to remove.
        var sub = await GetByIdAsync(id, ct);
        if (sub is null || sub.PendingVersionIndex is null) return;

        var pendingIdx = sub.PendingVersionIndex.Value;
        var pendingVersionId = sub.Versions[pendingIdx].VersionId;

        var filter = Builders<SignalSubscriptionDocument>.Filter.Eq(s => s.Id, id);
        var update = Builders<SignalSubscriptionDocument>.Update
            .PullFilter(s => s.Versions,
                Builders<RmeConfigurationVersion>.Filter.Eq(v => v.VersionId, pendingVersionId))
            .Set(s => s.PendingVersionIndex, null as int?)
            .Set(s => s.UpdatedAt, updatedAt);

        // Adjust current_version_index if needed (if we removed a version before it).
        if (pendingIdx <= sub.CurrentVersionIndex)
        {
            update = update.Set(s => s.CurrentVersionIndex, sub.CurrentVersionIndex - 1);
        }

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task ActivatePendingVersionAsync(
        ObjectId id, int newCurrentIndex, DateTime updatedAt, CancellationToken ct = default)
    {
        var filter = Builders<SignalSubscriptionDocument>.Filter.Eq(s => s.Id, id);
        var update = Builders<SignalSubscriptionDocument>.Update
            .Set(s => s.CurrentVersionIndex, newCurrentIndex)
            .Set(s => s.PendingVersionIndex, null as int?)
            .Set(s => s.UpdatedAt, updatedAt);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task<long> CountActiveByUserAsync(
        string userId, CancellationToken ct = default)
    {
        return await _collection.CountDocumentsAsync(
            s => s.UserId == userId && s.Status == SubscriptionStatus.Active,
            cancellationToken: ct);
    }

    public async Task<List<SignalSubscriptionDocument>> GetAllActiveAsync(
        CancellationToken ct = default)
    {
        return await _collection
            .Find(s => s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);
    }

    public async Task<List<SignalSubscriptionDocument>> GetActiveByUserAtAsync(
        string userId, DateTime at, CancellationToken ct = default)
    {
        // Returns subscriptions that were active at the given snapshot time.
        // Since pause/resume updates are immediate (MongoDB write), a subscription
        // is considered active at time T if status == "active" AND its last status
        // change (updated_at) was before or at T.
        return await _collection
            .Find(s => s.UserId == userId
                && s.Status == SubscriptionStatus.Active
                && s.UpdatedAt <= at)
            .ToListAsync(ct);
    }

    public async Task DeleteAsync(ObjectId id, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(s => s.Id == id, cancellationToken: ct);
    }
}
