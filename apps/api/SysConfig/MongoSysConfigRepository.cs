using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.SysConfig;

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

    private async Task<BsonDocument?> FindByKeyAsync(string key, CancellationToken ct)
    {
        return await _sysConfig
            .Find(Builders<BsonDocument>.Filter.Eq("key", key))
            .FirstOrDefaultAsync(ct);
    }
}
