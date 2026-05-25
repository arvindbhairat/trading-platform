using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Storage.SysConfig;

public sealed class MongoSysConfigRepository : ISysConfigRepository
{
    private readonly IMongoCollection<BsonDocument> _sysConfig;

    public MongoSysConfigRepository(IMongoDatabase database)
    {
        _sysConfig = database.GetCollection<BsonDocument>("sys_config");
    }

    public async Task<long> GetTesterCeilingAsync(CancellationToken ct = default)
    {
        const long defaultCeiling = 30;
        var doc = await FindByKeyAsync("operations.phase_a.tester_ceiling", ct);
        if (doc is null || !doc.Contains("value")) return defaultCeiling;

        var raw = doc["value"];
        return raw.IsInt32 ? raw.AsInt32
             : raw.IsInt64 ? raw.AsInt64
             : defaultCeiling;
    }

    public async Task<string> GetCurrentPhaseAsync(CancellationToken ct = default)
    {
        var doc = await FindByKeyAsync("operations.phase.current", ct);
        if (doc is null || !doc.Contains("value")) return "A";
        return doc["value"].AsString;
    }

    public async Task<string?> GetSeedVersionAsync(CancellationToken ct = default)
    {
        var doc = await FindByKeyAsync("platform.seed.version", ct);
        if (doc is null || !doc.Contains("value")) return null;
        return doc["value"].AsString;
    }

    public async Task<List<BsonDocument>> ListAllAsync(string? category = null, CancellationToken ct = default)
    {
        var filter = category is not null
            ? Builders<BsonDocument>.Filter.Eq("category", category)
            : Builders<BsonDocument>.Filter.Empty;

        return await _sysConfig
            .Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Ascending("key"))
            .ToListAsync(ct);
    }

    public async Task<BsonDocument?> GetByKeyAsync(string key, CancellationToken ct = default)
    {
        return await FindByKeyAsync(key, ct);
    }

    public async Task<BsonDocument?> UpdateAsync(string key, BsonValue newValue, string updatedByUserId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var update = Builders<BsonDocument>.Update
            .Set("value", newValue)
            .Set("updatedAt", now.ToString("o"))
            .Set("updatedByUserId", updatedByUserId)
            .Inc("version", 1);

        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            ReturnDocument = ReturnDocument.After
        };

        return await _sysConfig
            .FindOneAndUpdateAsync(
                Builders<BsonDocument>.Filter.Eq("key", key),
                update,
                options,
                ct);
    }

    public async Task<BsonDocument?> ResetToDefaultAsync(string key, string updatedByUserId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Read current doc to get defaultValue.
        var doc = await FindByKeyAsync(key, ct);
        if (doc is null || !doc.Contains("defaultValue")) return null;

        var defaultValue = doc["defaultValue"];
        var update = Builders<BsonDocument>.Update
            .Set("value", defaultValue)
            .Set("updatedAt", now.ToString("o"))
            .Set("updatedByUserId", updatedByUserId)
            .Inc("version", 1);

        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            ReturnDocument = ReturnDocument.After
        };

        return await _sysConfig
            .FindOneAndUpdateAsync(
                Builders<BsonDocument>.Filter.Eq("key", key),
                update,
                options,
                ct);
    }

    public async Task<List<string>> ListCategoriesAsync(CancellationToken ct = default)
    {
        var cursor = await _sysConfig
            .DistinctAsync<string>("category", Builders<BsonDocument>.Filter.Empty, cancellationToken: ct);
        return await cursor.ToListAsync(ct);
    }

    public async Task<decimal> GetDecimalAsync(string key, decimal defaultValue = 0m, CancellationToken ct = default)
    {
        var doc = await FindByKeyAsync(key, ct);
        if (doc is null || !doc.Contains("value")) return defaultValue;

        var raw = doc["value"];
        return raw.IsInt32 ? (decimal)raw.AsInt32
             : raw.IsInt64 ? (decimal)raw.AsInt64
             : raw.IsDouble ? (decimal)raw.AsDouble
             : defaultValue;
    }

    public async Task<long> GetLongAsync(string key, long defaultValue = 0, CancellationToken ct = default)
    {
        var doc = await FindByKeyAsync(key, ct);
        if (doc is null || !doc.Contains("value")) return defaultValue;

        var raw = doc["value"];
        return raw.IsInt32 ? raw.AsInt32
             : raw.IsInt64 ? raw.AsInt64
             : raw.IsDouble ? (long)raw.AsDouble
             : defaultValue;
    }

    private async Task<BsonDocument?> FindByKeyAsync(string key, CancellationToken ct)
    {
        return await _sysConfig
            .Find(Builders<BsonDocument>.Filter.Eq("key", key))
            .FirstOrDefaultAsync(ct);
    }
}
