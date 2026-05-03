using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Migrations.Migrations;

/// <summary>
/// Provisions every collection from <c>docs/data-management.md</c> with prescribed indexes,
/// TTL durations, and index options.  Online Archive policies are documented separately in
/// <c>atlas-online-archive-policies.json</c> and must be configured in the Atlas console.
/// Frozen-after-author: schema evolution flows through REQ-MIGRATION-001/004.
///
/// Idempotent: collections are created with <c>CreateCollectionAsync</c> only when they
/// do not already exist (checked via <c>ListCollectionNamesAsync</c>).  Index creation
/// with identical definitions is a no-op on re-run in MongoDB.
/// </summary>
public sealed class M001_InitialCollectionCatalogue : IMongoMigration
{
    public string Id => "M001";

    public Task DownAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
        => Task.CompletedTask; // No-op: migration is idempotent (collections/indexes created with IF-NOT-EXISTS semantics).

    public async Task UpAsync(IMongoDatabase database, CancellationToken cancellationToken = default)
    {
        var existingNames = (await (await database
            .ListCollectionNamesAsync(cancellationToken: cancellationToken))
            .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var spec in CollectionCatalogueSpec.All)
        {
            if (!existingNames.Contains(spec.Name))
                await database.CreateCollectionAsync(spec.Name, cancellationToken: cancellationToken);

            var col = database.GetCollection<BsonDocument>(spec.Name);

            foreach (var ttl in spec.TtlIndexes)
                await col.Indexes.CreateOneAsync(BuildTtlIndex(ttl), cancellationToken: cancellationToken);

            foreach (var sf in spec.SingleFieldIndexes)
                await col.Indexes.CreateOneAsync(BuildSingleFieldIndex(sf), cancellationToken: cancellationToken);

            foreach (var ci in spec.CompoundIndexes)
                await col.Indexes.CreateOneAsync(BuildCompoundIndex(ci), cancellationToken: cancellationToken);
        }
    }

    private static CreateIndexModel<BsonDocument> BuildTtlIndex(TtlIndexSpec spec)
    {
        var key = Builders<BsonDocument>.IndexKeys.Ascending(spec.FieldName);
        return new CreateIndexModel<BsonDocument>(key, new CreateIndexOptions
        {
            Name        = $"ttl_{spec.FieldName}",
            ExpireAfter = spec.Expiry,
            Sparse      = spec.Sparse,
        });
    }

    private static CreateIndexModel<BsonDocument> BuildSingleFieldIndex(SingleFieldIndexSpec spec)
    {
        var key = spec.Order >= 0
            ? Builders<BsonDocument>.IndexKeys.Ascending(spec.FieldName)
            : Builders<BsonDocument>.IndexKeys.Descending(spec.FieldName);

        return new CreateIndexModel<BsonDocument>(key, new CreateIndexOptions
        {
            Unique = spec.Unique,
            Sparse = spec.Sparse,
        });
    }

    private static CreateIndexModel<BsonDocument> BuildCompoundIndex(CompoundIndexSpec spec)
    {
        var parts = spec.Fields.Select(f =>
            f.Order >= 0
                ? Builders<BsonDocument>.IndexKeys.Ascending(f.Field)
                : Builders<BsonDocument>.IndexKeys.Descending(f.Field));

        return new CreateIndexModel<BsonDocument>(
            Builders<BsonDocument>.IndexKeys.Combine(parts),
            new CreateIndexOptions { Unique = spec.Unique, Sparse = spec.Sparse });
    }
}
