using SignalStack.Configuration.Ledger;

namespace SignalStack.Storage.LedgerWriters;

/// <summary>
/// Repository for the immutable <c>trade_ledger</c> MongoDB collection.
///
/// All write paths include the <c>ledger_fence_token</c> filter for stale-write
/// detection (REQ-PORT-031b(b)). Dedup is by <c>trade_id</c> per user
/// (REQ-PORT-023).
/// </summary>
public interface ITradeLedgerRepository
{
    /// <summary>
    /// Inserts a single trade into the ledger.
    /// If a trade with the same <c>TradeId</c> already exists for this user,
    /// the insert is silently skipped (idempotent dedup, REQ-PORT-023).
    /// All writes include the fencing-token filter (REQ-PORT-031b(b)).
    /// Returns <c>true</c> if inserted, <c>false</c> if duplicate.
    /// </summary>
    Task<bool> InsertTradeAsync(TradeLedgerDocument trade, CancellationToken ct = default);

    /// <summary>
    /// Inserts multiple trades. Duplicates are silently skipped.
    /// Each trade is written individually so that a duplicate does not
    /// block subsequent unique trades.
    /// Returns per-category counts.
    /// </summary>
    Task<BulkInsertResult> InsertTradesAsync(
        IReadOnlyList<TradeLedgerDocument> trades,
        CancellationToken ct = default);

    /// <summary>
    /// Checks whether a trade with the given <c>tradeId</c> already exists
    /// for the specified user.
    /// </summary>
    Task<bool> TradeExistsAsync(string userId, string tradeId, CancellationToken ct = default);

    /// <summary>
    /// Returns all trades for a user, ordered by <c>trade_at</c> ascending.
    /// Used for FIFO recomputation.
    /// </summary>
    Task<List<TradeLedgerDocument>> GetTradesByUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns all trades for a given user and symbol, ordered by <c>trade_at</c> ascending.
    /// Used for per-symbol FIFO recomputation.
    /// </summary>
    Task<List<TradeLedgerDocument>> GetTradesByUserAndSymbolAsync(
        string userId, string symbol, CancellationToken ct = default);
}

/// <summary>Result of a bulk insert operation.</summary>
public sealed record BulkInsertResult(int Inserted, int SkippedDuplicates);
