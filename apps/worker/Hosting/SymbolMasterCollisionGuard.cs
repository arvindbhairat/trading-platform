using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Worker.Hosting;

/// <summary>
/// Startup guard that validates no duplicate <c>sql_table_name_suffix</c> values
/// exist in the <c>symbol_master</c> collection (REQ-HIST-008a).
/// The Worker must refuse to start if any collisions are found, logging the
/// colliding symbols as a fatal startup error.
/// </summary>
public sealed class SymbolMasterCollisionGuard
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<SymbolMasterCollisionGuard> _logger;

    public SymbolMasterCollisionGuard(
        IMongoDatabase database,
        ILogger<SymbolMasterCollisionGuard> logger)
    {
        _database = database;
        _logger = logger;
    }

    public async Task VerifyAsync(CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Checking symbol_master for duplicate sql_table_name_suffix values");

        var symbolMaster = _database.GetCollection<BsonDocument>("symbol_master");

        var cursor = await symbolMaster.FindAsync(
            Builders<BsonDocument>.Filter.Empty,
            new FindOptions<BsonDocument>
            {
                Projection = Builders<BsonDocument>.Projection
                    .Include("symbol")
                    .Include("sql_table_name_suffix")
            },
            ct);

        var docs = await cursor.ToListAsync(ct);

        var collisions = FindCollisions(docs);

        if (collisions.Count == 0)
        {
            _logger.LogInformation(
                "Symbol master collision check passed — {SymbolCount} symbols, no duplicate suffixes.",
                docs.Count);
            return;
        }

        var collisionLines = collisions
            .Select(c => $"  suffix '{c.Suffix}' used by: {string.Join(", ", c.Symbols)}");

        var message = string.Join(
            Environment.NewLine,
            $"STARTUP BLOCKED: {collisions.Count} duplicate sql_table_name_suffix value(s) found "
                + "in symbol_master (REQ-HIST-008a). Refusing to start.",
            "Collisions:",
            string.Join(Environment.NewLine, collisionLines));

        _logger.LogCritical("{Message}", message);
        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// Finds duplicate <c>sql_table_name_suffix</c> values in a list of symbol documents.
    /// Extracted as a public static method for unit-testability.
    /// </summary>
    public static IReadOnlyList<SuffixCollision> FindCollisions(List<BsonDocument> docs)
    {
        if (docs.Count == 0) return ImmutableList<SuffixCollision>.Empty;

        return docs
            .GroupBy(d => d["sql_table_name_suffix"].AsString)
            .Where(g => g.Count() > 1)
            .Select(g => new SuffixCollision(
                g.Key,
                g.Select(d => d["symbol"].AsString).OrderBy(s => s).ToImmutableList()))
            .ToImmutableList();
    }
}

/// <summary>Describes a single suffix collision: the shared suffix and the colliding symbols.</summary>
public sealed record SuffixCollision(string Suffix, IReadOnlyList<string> Symbols);
