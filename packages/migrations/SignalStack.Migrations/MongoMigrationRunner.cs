using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Migrations;

/// <summary>
/// Discovers all <see cref="IMongoMigration"/> implementations in this assembly,
/// compares them against the <c>_migrations</c> tracking collection, and runs each
/// unapplied migration in ascending ID order.  Idempotent: re-running produces no changes
/// once a migration is recorded.
/// </summary>
public sealed class MongoMigrationRunner
{
    private const string MigrationsCollection = "_migrations";

    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoMigrationRunner> _logger;

    public MongoMigrationRunner(IMongoDatabase database, ILogger<MongoMigrationRunner> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var migrations = DiscoverMigrations();

        var tracking = _database.GetCollection<BsonDocument>(MigrationsCollection);
        var cursor = await tracking.FindAsync(Builders<BsonDocument>.Filter.Empty,
            cancellationToken: cancellationToken);
        var appliedIds = (await cursor.ToListAsync(cancellationToken))
            .Select(d => d["_id"].AsString)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var migration in migrations.Where(m => !appliedIds.Contains(m.Id)))
        {
            _logger.LogInformation("Applying migration {MigrationId}", migration.Id);

            await migration.UpAsync(_database, cancellationToken);

            await tracking.InsertOneAsync(
                new BsonDocument
                {
                    ["_id"]        = migration.Id,
                    ["applied_at"] = BsonDateTime.Create(DateTime.UtcNow),
                },
                cancellationToken: cancellationToken);

            _logger.LogInformation("Migration {MigrationId} applied", migration.Id);
        }
    }

    private static IOrderedEnumerable<IMongoMigration> DiscoverMigrations() =>
        typeof(MongoMigrationRunner).Assembly
            .GetTypes()
            .Where(t => typeof(IMongoMigration).IsAssignableFrom(t)
                        && !t.IsAbstract
                        && !t.IsInterface)
            .Select(t => (IMongoMigration)Activator.CreateInstance(t)!)
            .OrderBy(m => m.Id);
}
