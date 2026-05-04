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
}
