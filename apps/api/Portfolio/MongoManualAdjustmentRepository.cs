using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Portfolio;

/// <summary>
/// MongoDB repository for the <c>manual_adjustments</c> collection.
///
/// REQ-RECON-003: manual corrections recorded as explicit entries.
/// REQ-RECON-004: entries recorded against the affected holding/position context.
///
/// Indexes (defined in CollectionCatalogueSpec):
///   Compound: (user_id: 1, created_at: -1)
/// </summary>
public sealed class MongoManualAdjustmentRepository : IManualAdjustmentRepository
{
    private readonly IMongoCollection<ManualAdjustmentDocument> _collection;

    public MongoManualAdjustmentRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<ManualAdjustmentDocument>("manual_adjustments");
    }

    public async Task<ObjectId> InsertAsync(ManualAdjustmentDocument adjustment, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(adjustment, cancellationToken: ct);
        return adjustment.Id;
    }

    public async Task<List<ManualAdjustmentDocument>> GetByUserAsync(
        string userId, int limit = 50, CancellationToken ct = default)
    {
        return await _collection
            .Find(a => a.UserId == userId)
            .SortByDescending(a => a.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<List<ManualAdjustmentDocument>> GetByUserAndSymbolAsync(
        string userId, string symbol, int limit = 50, CancellationToken ct = default)
    {
        return await _collection
            .Find(a => a.UserId == userId && a.Symbol == symbol)
            .SortByDescending(a => a.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct);
    }

    public async Task<ManualAdjustmentDocument?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
    {
        return await _collection
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync(ct);
    }
}
