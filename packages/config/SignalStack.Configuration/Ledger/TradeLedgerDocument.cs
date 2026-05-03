using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Configuration.Ledger;

/// <summary>
/// Document model for the <c>trade_ledger</c> MongoDB collection.
///
/// Immutable append-only records of all trades synced from FYERS.
/// This is the accounting source of truth for FIFO and PnL calculations.
/// Records must never be deleted or modified after insertion — corrections
/// are recorded as explicit adjustment entries (REQ-RECON-003).
///
/// REQ-PORT-002, REQ-PORT-023.
/// </summary>
public sealed class TradeLedgerDocument
{
    [BsonId]
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();

    /// <summary>FYERS trade_id — dedup key, globally unique within a user's FYERS account. REQ-PORT-023.</summary>
    public required string TradeId { get; init; }

    /// <summary>Platform user ID.</summary>
    public required string UserId { get; init; }

    /// <summary>NSE trading symbol (e.g. "RELIANCE").</summary>
    public required string Symbol { get; init; }

    /// <summary>Side: "buy" or "sell".</summary>
    public required string Side { get; init; }

    /// <summary>Number of shares traded. Always positive (direction encoded in Side).</summary>
    public required int Quantity { get; init; }

    /// <summary>Execution price in INR.</summary>
    public required decimal Price { get; init; }

    /// <summary>Trade timestamp in UTC.</summary>
    public required DateTime TradeAt { get; init; }

    /// <summary>Related FYERS order ID, when available.</summary>
    public string? OrderId { get; init; }

    /// <summary>
    /// Source of this sync record.
    /// One of: "trade_history" (FYERS trade history API), "intraday" (current-day sync),
    /// "manual_adjustment" (REQ-RECON-003), "seeded_balance" (initial opening balance, REQ-PORT-015).
    /// </summary>
    public required string SyncSource { get; init; }

    /// <summary>When the platform ingested this trade (UTC).</summary>
    public required DateTime IngestedAt { get; init; }

    /// <summary>
    /// Fencing token from the lock acquisition protecting this write.
    /// Every trade-ledger write includes the <c>ledger_fence_token</c> filter for
    /// stale-write detection (REQ-PORT-031b(b)).
    /// </summary>
    public required long LedgerFenceToken { get; init; }

    /// <summary>
    /// True when this entry is a manual adjustment (REQ-RECON-003) or seeded opening balance (REQ-PORT-015),
    /// not a broker-synced trade. Adjustment entries are explicitly flagged and audited.
    /// </summary>
    public bool IsAdjustment { get; init; }

    /// <summary>
    /// Contextual reference for adjustment entries.
    /// Maps to the affected holding or position per REQ-RECON-004.
    /// For manual adjustments, references the <c>manual_adjustments</c> collection document ID.
    /// </summary>
    public string? AdjustmentContext { get; init; }

    /// <summary>Optional notes — e.g. adjustment reason, admin comments.</summary>
    public string? Notes { get; init; }
}
