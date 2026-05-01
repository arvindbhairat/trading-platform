using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Universe;

public sealed class MongoUniverseUploadRepository : IUniverseUploadRepository
{
    private readonly IMongoCollection<UniverseUploadResultDocument> _collection;

    public MongoUniverseUploadRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<UniverseUploadResultDocument>("universe_upload_results");
    }

    public async Task CreateAsync(UniverseUploadResultDocument document, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task<UniverseUploadResultDocument?> GetLatestAsync(CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<UniverseUploadResultDocument>.Filter.Empty)
            .Sort(Builders<UniverseUploadResultDocument>.Sort.Descending(u => u.UploadedAt))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<UniverseUploadResultDocument>> GetAllAsync(CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<UniverseUploadResultDocument>.Filter.Empty)
            .Sort(Builders<UniverseUploadResultDocument>.Sort.Descending(u => u.UploadedAt))
            .ToListAsync(ct);
    }

    public async Task<UniverseUploadResultDocument?> FindByIdAsync(ObjectId id, CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<UniverseUploadResultDocument>.Filter.Eq(u => u.Id, id))
            .FirstOrDefaultAsync(ct);
    }

    public async Task MarkRolledBackAsync(
        ObjectId id, string rolledBackBy, DateTime rolledBackAt, CancellationToken ct = default)
    {
        var filter = Builders<UniverseUploadResultDocument>.Filter.Eq(u => u.Id, id);
        var update = Builders<UniverseUploadResultDocument>.Update
            .Set(u => u.RolledBack, true)
            .Set(u => u.RolledBackAt, rolledBackAt)
            .Set(u => u.RolledBackBy, rolledBackBy);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task<UniverseSnapshot?> GetSnapshotAsync(ObjectId id, CancellationToken ct = default)
    {
        var doc = await _collection
            .Find(Builders<UniverseUploadResultDocument>.Filter.Eq(u => u.Id, id))
            .Project(u => u.Snapshot)
            .FirstOrDefaultAsync(ct);
        return doc;
    }

    public async Task PruneSnapshotsAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var filter = Builders<UniverseUploadResultDocument>.Filter.Lt(u => u.UploadedAt, cutoff);
        var update = Builders<UniverseUploadResultDocument>.Update.Unset("snapshot");
        await _collection.UpdateManyAsync(filter, update, cancellationToken: ct);
    }
}
