using MongoDB.Driver;

namespace SignalStack.Api.Execution;

/// <summary>
/// Abstraction over the MongoDB <c>intent_ledger</c> collection.
/// Supports creation of intent records with nonce uniqueness enforcement
/// via the MongoDB unique index, and status transitions for callback and
/// LADS reconciliation paths.
/// P7-T6 / REQ-ORDER-014a/014b.
/// </summary>
public interface IIntentLedgerRepository
{
    /// <summary>
    /// Creates a new intent record with status <c>pending</c>.
    /// The <c>nonce</c> unique index enforces single-use — a
    /// <see cref="MongoWriteException"/> with code 11000 (DuplicateKey)
    /// is thrown if the nonce already exists.
    /// </summary>
    Task CreateIntentAsync(IntentLedgerDocument intent, CancellationToken ct = default);
}
