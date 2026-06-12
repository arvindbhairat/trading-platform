using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Migrations;

namespace SignalStack.Seed;

/// <summary>
/// Provisions TTL indexes on existing MongoDB collections based on
/// <see cref="CollectionCatalogueSpec"/>. Idempotent: skips indexes that already exist.
///
/// Designed to run as a post-seed step in the seed-sys-config GitHub Action so the
/// build log shows collection inventory and TTL index status before Railway deploys.
/// </summary>
public sealed class TtlIndexProvisioningService
{
    private readonly IMongoDatabase _database;

    public TtlIndexProvisioningService(string connectionString, string databaseName)
    {
        var client = new MongoClient(connectionString);
        _database = client.GetDatabase(databaseName);
    }

    public async Task<TtlIndexReport> RunAsync(CancellationToken ct = default)
    {
        // ── 1. List all collections in the database ──────────────────────────
        var existingNames = (await (await _database
            .ListCollectionNamesAsync(cancellationToken: ct))
            .ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var specNames = CollectionCatalogueSpec.All
            .Select(s => s.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var present = new List<string>();
        var missing = new List<string>();
        var ttlCreated = new List<string>();
        var ttlSkipped = new List<string>();

        // ── 2. Collection inventory ──────────────────────────────────────────
        Console.Error.WriteLine();
        Console.Error.WriteLine(new string('\u2550', 70));
        Console.Error.WriteLine("  MongoDB Collection Inventory");
        Console.Error.WriteLine(new string('\u2550', 70));

        foreach (var spec in CollectionCatalogueSpec.All)
        {
            var exists = existingNames.Contains(spec.Name);
            if (exists)
            {
                present.Add(spec.Name);
                Console.Error.WriteLine($"  \u2713 {spec.Name}");
            }
            else
            {
                missing.Add(spec.Name);
                Console.Error.WriteLine($"  \u2717 {spec.Name}  (NOT YET CREATED)");
            }
        }

        // ── 3. Unknown collections (in DB but not in spec) ───────────────────
        var unknown = existingNames
            .Where(n => !specNames.Contains(n) && !n.StartsWith('_'))
            .OrderBy(n => n)
            .ToList();

        if (unknown.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("  \u2500\u2500 Unknown Collections (not in spec) \u2500\u2500");
            foreach (var name in unknown)
                Console.Error.WriteLine($"  ? {name}");
        }

        // ── 4. TTL index provisioning ────────────────────────────────────────
        Console.Error.WriteLine();
        Console.Error.WriteLine(new string('\u2550', 70));
        Console.Error.WriteLine("  TTL Index Provisioning");
        Console.Error.WriteLine(new string('\u2550', 70));

        foreach (var spec in CollectionCatalogueSpec.All)
        {
            if (spec.TtlIndexes.Count == 0)
            {
                if (existingNames.Contains(spec.Name))
                    Console.Error.WriteLine($"  \u2014  {spec.Name}: no TTL indexes required");
                continue;
            }

            if (!existingNames.Contains(spec.Name))
            {
                Console.Error.WriteLine($"  SKIP {spec.Name}: collection does not exist yet");
                continue;
            }

            var col = _database.GetCollection<BsonDocument>(spec.Name);

            // Fetch existing index names for this collection
            var existingIndexNames = (await (await col.Indexes
                .ListAsync(cancellationToken: ct))
                .ToListAsync(ct))
                .Select(idx => idx["name"].AsString)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var ttl in spec.TtlIndexes)
            {
                var indexName = $"ttl_{ttl.FieldName}";

                if (existingIndexNames.Contains(indexName))
                {
                    ttlSkipped.Add($"{spec.Name}.{ttl.FieldName}");
                    Console.Error.WriteLine($"  SKIP {spec.Name}.{indexName}: already exists");
                }
                else
                {
                    var keys = Builders<BsonDocument>.IndexKeys.Ascending(ttl.FieldName);
                    await col.Indexes.CreateOneAsync(
                        new CreateIndexModel<BsonDocument>(keys, new CreateIndexOptions
                        {
                            Name = indexName,
                            ExpireAfter = ttl.Expiry,
                            Sparse = ttl.Sparse,
                        }),
                        cancellationToken: ct);

                    ttlCreated.Add($"{spec.Name}.{ttl.FieldName}");
                    var expiryDesc = FormatExpiry(ttl.Expiry);
                    Console.Error.WriteLine(
                        $"  CREATE {spec.Name}.{indexName}: expire after {expiryDesc}{(ttl.Sparse ? " (sparse)" : "")}");
                }
            }
        }

        return new TtlIndexReport(present, missing, unknown, ttlCreated, ttlSkipped);
    }

    private static string FormatExpiry(TimeSpan expiry)
    {
        if (expiry == TimeSpan.FromSeconds(86_400))    return "24h";
        if (expiry == TimeSpan.FromSeconds(2_592_000))  return "30d";
        if (expiry == TimeSpan.FromSeconds(15_552_000)) return "180d (6mo)";
        return $"{expiry.TotalSeconds}s";
    }
}

public sealed record TtlIndexReport(
    IReadOnlyList<string> PresentCollections,
    IReadOnlyList<string> MissingCollections,
    IReadOnlyList<string> UnknownCollections,
    IReadOnlyList<string> TtlIndexesCreated,
    IReadOnlyList<string> TtlIndexesSkipped);
