using MongoDB.Driver;
using SignalStack.Domain.Execution;

namespace SignalStack.Storage.Execution;

public sealed class MongoIntentLedgerRepository : IIntentLedgerRepository
{
    private readonly IMongoCollection<IntentLedgerDocument> _intents;

    public MongoIntentLedgerRepository(IMongoDatabase database)
    {
        _intents = database.GetCollection<IntentLedgerDocument>("intent_ledger");
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<(long CallbackMatchedCount, long LadsOnlyMatchedCount)> GetReconciliationCountsAsync(
        TimeSpan window, CancellationToken ct = default)
    {
        var since = DateTime.UtcNow.Subtract(window);

        var callbackFilter = Builders<IntentLedgerDocument>.Filter.And(
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.MatchSource, "callback"),
            Builders<IntentLedgerDocument>.Filter.Gte(d => d.CreatedAt, since));

        var ladsOnlyFilter = Builders<IntentLedgerDocument>.Filter.And(
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.MatchSource, "lads_reconciliation"),
            Builders<IntentLedgerDocument>.Filter.Gte(d => d.CreatedAt, since));

        var callbackCount = await _intents.CountDocumentsAsync(callbackFilter, cancellationToken: ct);
        var ladsOnlyCount = await _intents.CountDocumentsAsync(ladsOnlyFilter, cancellationToken: ct);

        return (callbackCount, ladsOnlyCount);
    }

    /// <inheritdoc />
    public async Task<List<IntentLedgerDocument>> GetAwaitingConfirmationByUserAsync(
        string userId, string? symbol = null, CancellationToken ct = default)
    {
        var filters = new List<FilterDefinition<IntentLedgerDocument>>
        {
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.UserId, userId),
            Builders<IntentLedgerDocument>.Filter.Eq(d => d.Status, IntentStatus.Matched),
        };

        if (symbol is not null)
            filters.Add(Builders<IntentLedgerDocument>.Filter.Eq(d => d.Symbol, symbol));

        return await _intents.Find(Builders<IntentLedgerDocument>.Filter.And(filters))
            .Sort(Builders<IntentLedgerDocument>.Sort.Descending(d => d.CreatedAt))
            .ToListAsync(ct);
    }
}
