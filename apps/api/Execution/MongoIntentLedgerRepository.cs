using MongoDB.Driver;

namespace SignalStack.Api.Execution;

public sealed class MongoIntentLedgerRepository : IIntentLedgerRepository
{
    private readonly IMongoCollection<IntentLedgerDocument> _intents;

    public MongoIntentLedgerRepository(IMongoDatabase database)
    {
        _intents = database.GetCollection<IntentLedgerDocument>("intent_ledger");
    }

    /// <inheritdoc />
    /// <remarks>
    /// Nonce uniqueness is enforced by the MongoDB unique index on the <c>nonce</c> field.
    /// A <see cref="MongoWriteException"/> with <c>code 11000 (DuplicateKey)</c>
    /// is thrown when a nonce collision occurs — this is expected to be extremely rare
    /// given the 128-bit random nonce, and the caller should treat it as a transient
    /// error suitable for retry with a fresh nonce.
    /// </remarks>
    public async Task CreateIntentAsync(IntentLedgerDocument intent, CancellationToken ct = default)
    {
        await _intents.InsertOneAsync(intent, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<IntentLedgerDocument?> UpdateIntentFromCallbackAsync(
        string nonce, string status, string? requestToken, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var update = Builders<IntentLedgerDocument>.Update
            .Set(d => d.Status, status)
            .Set(d => d.MatchSource, "callback")
            .Set(d => d.CallbackReceivedAt, now)
            .Set(d => d.UpdatedAt, now);

        if (requestToken is not null)
            update = update.Set(d => d.RequestToken, requestToken);

        var filter = Builders<IntentLedgerDocument>.Filter.Eq(d => d.Nonce, nonce);

        var options = new FindOneAndUpdateOptions<IntentLedgerDocument>
        {
            ReturnDocument = ReturnDocument.After,
        };

        try
        {
            return await _intents.FindOneAndUpdateAsync(filter, update, options, ct);
        }
        catch (MongoWriteException)
        {
            return null;
        }
    }

    // ── LADS reconciliation methods (P7-T8 / REQ-ORDER-015c) ────────────────

    /// <inheritdoc />
    public async Task<List<IntentLedgerDocument>> GetPendingIntentsByUserAsync(
        string userId, CancellationToken ct = default)
    {
        var filter = Builders<IntentLedgerDocument>.Filter.And(
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.UserId, userId),
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.Status, IntentStatus.Pending));

        return await _intents.Find(filter)
            .Sort(Builders<IntentLedgerDocument>.Sort.Descending(d => d.CreatedAt))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<List<IntentLedgerDocument>> GetUnreconciledMatchedIntentsByUserAsync(
        string userId, CancellationToken ct = default)
    {
        var filter = Builders<IntentLedgerDocument>.Filter.And(
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.UserId, userId),
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.Status, IntentStatus.Matched),
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.MatchSource, "callback"),
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.ReconciledAt, null));

        return await _intents.Find(filter)
            .Sort(Builders<IntentLedgerDocument>.Sort.Descending(d => d.CreatedAt))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task ReconcileFromLadsAsync(
        string nonce, string? brokerOrderId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var update = Builders<IntentLedgerDocument>.Update
            .Set(d => d.Status, IntentStatus.Matched)
            .Set(d => d.MatchSource, "lads_reconciliation")
            .Set(d => d.ReconciledAt, now)
            .Set(d => d.UpdatedAt, now);

        if (brokerOrderId is not null)
            update = update.Set(d => d.MatchedBrokerOrderId, brokerOrderId);

        var filter = Builders<IntentLedgerDocument>.Filter.Eq(d => d.Nonce, nonce);

        await _intents.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task StampReconciledAtAsync(string nonce, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var update = Builders<IntentLedgerDocument>.Update
            .Set(d => d.ReconciledAt, now)
            .Set(d => d.UpdatedAt, now);

        var filter = Builders<IntentLedgerDocument>.Filter.Eq(d => d.Nonce, nonce);

        await _intents.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task MarkUnresolvedAsync(string nonce, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var update = Builders<IntentLedgerDocument>.Update
            .Set(d => d.Status, IntentStatus.Unresolved)
            .Set(d => d.UnresolvedAt, now)
            .Set(d => d.UpdatedAt, now);

        var filter = Builders<IntentLedgerDocument>.Filter.Eq(d => d.Nonce, nonce);

        await _intents.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <inheritdoc />
    public async Task<List<IntentLedgerDocument>> GetAllIntentsByUserAsync(
        string userId, CancellationToken ct = default)
    {
        var filter = Builders<IntentLedgerDocument>.Filter.Eq(d => d.UserId, userId);

        return await _intents.Find(filter)
            .Sort(Builders<IntentLedgerDocument>.Sort.Descending(d => d.CreatedAt))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task CreateOrphanAckAsync(
        IntentLedgerDocument orphanIntent, CancellationToken ct = default)
    {
        await _intents.InsertOneAsync(orphanIntent, cancellationToken: ct);
    }
}
