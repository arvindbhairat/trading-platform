using MongoDB.Driver;

namespace SignalStack.Api.Execution;

/// <summary>
/// Abstraction over the MongoDB <c>intent_ledger</c> collection.
/// Supports creation of intent records with nonce uniqueness enforcement
/// via the MongoDB unique index, and status transitions for callback and
/// LADS reconciliation paths.
/// P7-T6 / REQ-ORDER-014a/014b.
/// P7-T8 / REQ-ORDER-015c: LADS intent-reconciliation safety net methods.
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

    /// <summary>
    /// Updates an intent record based on the FYERS widget <c>finished</c> callback.
    /// Sets status to <c>matched</c> or <c>submission_failed</c>, records
    /// <c>match_source = "callback"</c>, and persists <c>request_token</c>
    /// and <c>callback_received_at</c>.
    /// P7-T7 / REQ-ORDER-015/015e/015f.
    /// </summary>
    /// <returns>The matched intent document, or null if the nonce was not found.</returns>
    Task<IntentLedgerDocument?> UpdateIntentFromCallbackAsync(
        string nonce, string status, string? requestToken, CancellationToken ct = default);

    // ── LADS reconciliation methods (P7-T8 / REQ-ORDER-015c) ────────────────

    /// <summary>
    /// Returns all intents for the given user that are still in <c>pending</c>
    /// status. Used by LADS reconciliation to detect callback-missed intents
    /// and stale intents eligible for timeout.
    /// </summary>
    Task<List<IntentLedgerDocument>> GetPendingIntentsByUserAsync(
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns all intents for the given user with <c>matched</c> status
    /// and <c>match_source = "callback"</c> that have no <c>reconciled_at</c>
    /// stamp yet. Used by LADS to stamp <c>reconciled_at</c> on callback-matched
    /// intents when a matching trade is observed.
    /// </summary>
    Task<List<IntentLedgerDocument>> GetUnreconciledMatchedIntentsByUserAsync(
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Transitions a <c>pending</c> intent to <c>matched</c> with
    /// <c>match_source = "lads_reconciliation"</c> and records the observed
    /// broker order ID. Called when LADS finds a trade matching a pending
    /// intent but no callback was received (callback-missed path).
    /// </summary>
    Task ReconcileFromLadsAsync(
        string nonce, string? brokerOrderId, CancellationToken ct = default);

    /// <summary>
    /// Stamps <c>reconciled_at</c> on a callback-matched intent when LADS
    /// observes a confirming trade. Does not change <c>status</c> or
    /// <c>match_source</c>.
    /// </summary>
    Task StampReconciledAtAsync(string nonce, CancellationToken ct = default);

    /// <summary>
    /// Transitions a <c>pending</c> intent to <c>unresolved</c> after
    /// <c>orders.intent_timeout_minutes</c> elapsed with no observed
    /// FYERS order. Sets <c>unresolved_at</c> and <c>updated_at</c>.
    /// </summary>
    Task MarkUnresolvedAsync(string nonce, CancellationToken ct = default);

    /// <summary>
    /// Returns all intents for the given user regardless of status.
    /// Used by orphan detection to determine whether a broker order ID
    /// is already matched to an intent.
    /// </summary>
    Task<List<IntentLedgerDocument>> GetAllIntentsByUserAsync(
        string userId, CancellationToken ct = default);

    /// <summary>
    /// Creates a new intent record with <c>status = "orphan_ack"</c> to
    /// document an FYERS order or trade observed by LADS that has no
    /// corresponding platform intent. Uses a synthetic nonce prefixed
    /// with <c>orphan_</c> derived from the broker order ID.
    /// </summary>
    Task CreateOrphanAckAsync(
        IntentLedgerDocument orphanIntent, CancellationToken ct = default);

    /// <summary>
    /// Returns all intents for the given user with <c>matched</c> status
    /// whose corresponding position is still in <c>PendingEntry</c>.
    /// These are intents awaiting fill confirmation from LADS (REQ-ORDER-015e).
    /// </summary>
    Task<List<IntentLedgerDocument>> GetAwaitingConfirmationByUserAsync(
        string userId, string? symbol = null, CancellationToken ct = default);
}
