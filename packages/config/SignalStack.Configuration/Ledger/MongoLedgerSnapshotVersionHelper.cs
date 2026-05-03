using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Configuration.Ledger;

/// <summary>
/// MongoDB implementation of <see cref="ILedgerSnapshotVersionHelper"/>.
///
/// Atomically increments the <c>ledger_snapshot_version</c> field on the user
/// document in the <c>users</c> collection after a durable trade-ledger write
/// has committed (REQ-PORT-031a).
///
/// Invariant: this helper must only be called after all trade-ledger writes
/// have durably committed, never on a fencing-token abort or lock-renewal
/// failure. See <see cref="ILedgerSnapshotVersionHelper"/> for the full
/// invariant contract.
/// </summary>
public sealed class MongoLedgerSnapshotVersionHelper : ILedgerSnapshotVersionHelper
{
    private readonly IMongoCollection<BsonDocument> _usersCollection;
    private readonly ILogger<MongoLedgerSnapshotVersionHelper> _logger;

    public MongoLedgerSnapshotVersionHelper(
        IMongoDatabase database,
        ILogger<MongoLedgerSnapshotVersionHelper> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        _usersCollection = database.GetCollection<BsonDocument>("users");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task IncrementAsync(string userId, CancellationToken cancellationToken)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var update = Builders<BsonDocument>.Update
            .Inc("ledger_snapshot_version", 1L)
            .Set("updated_at", DateTime.UtcNow);

        var result = await _usersCollection.UpdateOneAsync(
            filter, update, cancellationToken: cancellationToken);

        if (result.MatchedCount == 0)
        {
            _logger.LogWarning(
                "ledger_snapshot_version increment for user {UserId} matched 0 documents. " +
                "User may not exist yet.",
                userId);
        }

        _logger.LogTrace(
            "ledger_snapshot_version incremented for user {UserId}. " +
            "Matched: {Matched}, Modified: {Modified}.",
            userId, result.MatchedCount, result.ModifiedCount);
    }
}
