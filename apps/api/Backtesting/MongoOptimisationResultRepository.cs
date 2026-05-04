using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// MongoDB implementation of IOptimisationResultRepository.
/// Collection: optimisation_results.
/// Indexed on (user_id, subscription_id, symbol) for fast lookups.
/// </summary>
public sealed class MongoOptimisationResultRepository : IOptimisationResultRepository
{
    private readonly IMongoCollection<OptimisationResultDocument> _collection;

    public MongoOptimisationResultRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<OptimisationResultDocument>("optimisation_results");

        // Ensure indexes for common queries
        var indexKeys = Builders<OptimisationResultDocument>.IndexKeys
            .Ascending(r => r.SubscriptionId)
            .Ascending(r => r.Symbol)
            .Descending(r => r.CompletedAt);

        var indexOptions = new CreateIndexOptions { Background = true };
        _collection.Indexes.CreateMany([
            new CreateIndexModel<OptimisationResultDocument>(indexKeys, indexOptions),
            new CreateIndexModel<OptimisationResultDocument>(
                Builders<OptimisationResultDocument>.IndexKeys
                    .Ascending(r => r.UserId)
                    .Descending(r => r.CompletedAt), indexOptions),
        ]);
    }

    public async Task SaveAsync(OptimisationResultDocument document, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task UpdateCompletionAsync(
        ObjectId runId,
        List<ProfileConfigResult> results,
        DateTime completedAt,
        CancellationToken ct = default)
    {
        await _collection.UpdateOneAsync(
            Builders<OptimisationResultDocument>.Filter.Eq(r => r.Id, runId),
            Builders<OptimisationResultDocument>.Update
                .Set(r => r.Status, "completed")
                .Set(r => r.CompletedAt, completedAt)
                .Set(r => r.Results, results),
            cancellationToken: ct);
    }

    public async Task MarkFailedAsync(ObjectId runId, CancellationToken ct = default)
    {
        await _collection.UpdateOneAsync(
            Builders<OptimisationResultDocument>.Filter.Eq(r => r.Id, runId),
            Builders<OptimisationResultDocument>.Update
                .Set(r => r.Status, "failed")
                .Set(r => r.CompletedAt, DateTime.UtcNow),
            cancellationToken: ct);
    }

    public async Task<OptimisationResultDocument?> GetLatestAsync(
        ObjectId subscriptionId, string symbol, CancellationToken ct = default)
    {
        return await _collection
            .Find(r => r.SubscriptionId == subscriptionId
                       && r.Symbol == symbol
                       && r.Status == "completed")
            .SortByDescending(r => r.CompletedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<OptimisationResultDocument?> GetActiveRunAsync(
        ObjectId subscriptionId, string symbol, CancellationToken ct = default)
    {
        return await _collection
            .Find(r => r.SubscriptionId == subscriptionId
                       && r.Symbol == symbol
                       && r.Status == "running")
            .FirstOrDefaultAsync(ct);
    }

    public async Task<OptimisationResultDocument?> GetByIdAsync(ObjectId runId, CancellationToken ct = default)
    {
        return await _collection
            .Find(r => r.Id == runId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<OptimisationResultDocument>> ListByUserAsync(
        string userId, int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        return await _collection
            .Find(r => r.UserId == userId)
            .SortByDescending(r => r.TriggeredAt)
            .Skip(offset)
            .Limit(limit)
            .ToListAsync(ct);
    }
}
