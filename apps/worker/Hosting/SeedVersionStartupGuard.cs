using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Worker.Hosting;

/// <summary>
/// Startup guard that verifies the <c>platform.seed.version</c> sentinel row
/// exists in <c>sys_config</c> before the Worker begins processing (REQ-CONFIG-010).
/// Prevents the Worker from activating when the seeder step was skipped.
/// </summary>
public sealed class SeedVersionStartupGuard
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<SeedVersionStartupGuard> _logger;

    public SeedVersionStartupGuard(IMongoDatabase database, ILogger<SeedVersionStartupGuard> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task VerifyAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Verifying platform.seed.version sentinel row in sys_config");

        var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
        var filter = Builders<BsonDocument>.Filter.Eq("key", "platform.seed.version");
        var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

        if (doc is null || !doc.Contains("value"))
        {
            _logger.LogCritical(
                "STARTUP BLOCKED: sys_config is missing platform.seed.version — " +
                "the seeder step has not been run. " +
                "Deployment pipeline must run the seeder before activating the Worker. " +
                "See REQ-CONFIG-010.");
            throw new InvalidOperationException(
                "sys_config is missing platform.seed.version — seeder step was skipped. " +
                "Run the seeder before deploying the Worker.");
        }

        var version = doc["value"].AsString;
        _logger.LogInformation("Seed version verified: {SeedVersion}", version);
    }
}
