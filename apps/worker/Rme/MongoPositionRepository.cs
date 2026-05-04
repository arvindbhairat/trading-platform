using MongoDB.Driver;

namespace SignalStack.Worker.Rme;

/// <summary>
/// MongoDB implementation of <see cref="IPositionRepository"/> with <c>_version</c>-based
/// Optimistic Concurrency Control (REQ-RME-CONC-002) and fencing-token stale-write
/// detection (REQ-PORT-031b(b)).
/// </summary>
internal sealed class MongoPositionRepository : IPositionRepository
{
    private readonly IMongoCollection<PositionDocument> _collection;

    public MongoPositionRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<PositionDocument>("positions");
    }

    public async Task<PositionDocument?> GetByIdAsync(
        Guid positionId, CancellationToken ct = default)
    {
        return await _collection
            .Find(p => p.PositionId == positionId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task CreateAsync(
        PositionDocument position, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(position, cancellationToken: ct);
    }

    public async Task<PositionOccResult> UpdateWithOccAsync(
        PositionDocument position,
        long expectedVersion,
        long? fencingToken = null,
        CancellationToken ct = default)
    {
        // Build filter: { _id: positionId, _version: expectedVersion }
        var filter = Builders<PositionDocument>.Filter.Eq(p => p.PositionId, position.PositionId)
                   & Builders<PositionDocument>.Filter.Eq("_version", expectedVersion);

        // Extend with fencing-token guard when provided (REQ-PORT-031b(b))
        if (fencingToken.HasValue)
        {
            filter = filter & Builders<PositionDocument>.Filter.Lte(
                p => p.LedgerFenceToken, fencingToken.Value);
        }

        // Increment version for the replacement document
        position.Version = expectedVersion + 1;
        position.UpdatedAt = DateTime.UtcNow;

        var result = await _collection.ReplaceOneAsync(filter, position, cancellationToken: ct);

        if (result.MatchedCount == 0)
        {
            // OCC conflict — revert the version increment
            position.Version = expectedVersion;
            return new PositionOccResult(false, 0);
        }

        return new PositionOccResult(true, position.Version);
    }

    public async Task<List<PositionDocument>> GetNonTerminalAsync(
        CancellationToken ct = default)
    {
        return await _collection
            .Find(p => p.State != PositionState.Closed && p.State != PositionState.Rejected)
            .ToListAsync(ct);
    }

    public async Task<List<PositionDocument>> GetNonTerminalByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        return await _collection
            .Find(p => p.UserId == userId
                    && p.State != PositionState.Closed
                    && p.State != PositionState.Rejected)
            .ToListAsync(ct);
    }

    public async Task<List<PositionDocument>> GetByUserIdAndStateAsync(
        string userId, PositionState state, CancellationToken ct = default)
    {
        return await _collection
            .Find(p => p.UserId == userId && p.State == state)
            .ToListAsync(ct);
    }
}
