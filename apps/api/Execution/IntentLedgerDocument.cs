using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Execution;

/// <summary>
/// Document model for the <c>intent_ledger</c> MongoDB collection.
/// Per-user order intent records written synchronously before the FYERS API Connect
/// widget activates (REQ-ORDER-014a). Each record is the platform's authoritative
/// pre-submission view of what the user attempted to submit. Two downstream paths
/// update the record: the widget <c>finished</c> callback (REQ-ORDER-015) and the
/// LADS reconciliation safety net (REQ-ORDER-015c).
/// P7-T6 / REQ-ORDER-014a/014b.
/// </summary>
public sealed class IntentLedgerDocument
{
    [BsonId]
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();

    /// <summary>Platform user ID.</summary>
    [BsonElement("user_id")]
    public required string UserId { get; init; }

    /// <summary>Platform position ID when the intent references an existing position (add/reduce/exit).</summary>
    [BsonElement("position_id")]
    [BsonIgnoreIfNull]
    public string? PositionId { get; init; }

    /// <summary>Signal subscription ID when the intent originates from a platform signal advisory.</summary>
    [BsonElement("signal_id")]
    [BsonIgnoreIfNull]
    public string? SignalId { get; init; }

    /// <summary>Intent action: Entry, Add, Reduce, or Exit.</summary>
    [BsonElement("action")]
    public required string Action { get; init; }

    /// <summary>NSE trading symbol (e.g. "RELIANCE").</summary>
    [BsonElement("symbol")]
    public required string Symbol { get; init; }

    /// <summary>Side: BUY or SELL.</summary>
    [BsonElement("side")]
    public required string Side { get; init; }

    /// <summary>Number of shares.</summary>
    [BsonElement("quantity")]
    public required int Quantity { get; init; }

    /// <summary>Order type: MARKET or LIMIT.</summary>
    [BsonElement("order_type")]
    public required string OrderType { get; init; }

    /// <summary>Limit price in INR (0 for MARKET orders).</summary>
    [BsonElement("price")]
    public required decimal Price { get; init; }

    /// <summary>Product type: always CNC per REQ-ORDER-010.</summary>
    [BsonElement("product")]
    public required string Product { get; init; }

    /// <summary>
    /// Intent status: pending, matched, submission_failed, unresolved, or orphan_ack.
    /// Written as "pending" on creation; updated by callback or LADS reconciliation.
    /// </summary>
    [BsonElement("status")]
    public required string Status { get; set; }

    /// <summary>How the intent was matched: "callback" or "lads_reconciliation". Null for non-terminal states.</summary>
    [BsonElement("match_source")]
    [BsonIgnoreIfNull]
    public string? MatchSource { get; set; }

    /// <summary>Request token from the FYERS widget callback, set on successful callback.</summary>
    [BsonElement("request_token")]
    [BsonIgnoreIfNull]
    public string? RequestToken { get; set; }

    /// <summary>
    /// FYERS broker order ID resolved either from the callback payload or from LADS reconciliation.
    /// </summary>
    [BsonElement("matched_broker_order_id")]
    [BsonIgnoreIfNull]
    public string? MatchedBrokerOrderId { get; set; }

    /// <summary>UTC timestamp when the widget callback was received.</summary>
    [BsonElement("callback_received_at")]
    [BsonIgnoreIfNull]
    public DateTime? CallbackReceivedAt { get; set; }

    /// <summary>UTC timestamp when LADS reconciliation processed this intent.</summary>
    [BsonElement("reconciled_at")]
    [BsonIgnoreIfNull]
    public DateTime? ReconciledAt { get; set; }

    /// <summary>UTC timestamp when the intent record was created.</summary>
    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }

    /// <summary>UTC timestamp of the last status change.</summary>
    [BsonElement("updated_at")]
    public required DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Single-use server-issued nonce that bound the signed order payload (REQ-ORDER-009b).
    /// Persisted to prevent nonce replay. Enforced unique by the MongoDB index.
    /// </summary>
    [BsonElement("nonce")]
    public required string Nonce { get; init; }

    /// <summary>HMAC signature of the signed order payload, retained for audit.</summary>
    [BsonElement("payload_signature")]
    public required string PayloadSignature { get; init; }
}

/// <summary>
/// Canonical status values for the <c>intent_ledger</c> collection.
/// </summary>
public static class IntentStatus
{
    /// <summary>Written before widget activation; awaiting callback or LADS reconciliation.</summary>
    public const string Pending = "pending";

    /// <summary>Widget callback confirmed the submission succeeded.</summary>
    public const string Matched = "matched";

    /// <summary>Widget callback indicated the submission failed.</summary>
    public const string SubmissionFailed = "submission_failed";

    /// <summary>Intent timed out without resolution; flagged for review.</summary>
    public const string Unresolved = "unresolved";

    /// <summary>Orphan intent acknowledged by admin or automatic cleanup.</summary>
    public const string OrphanAck = "orphan_ack";
}
