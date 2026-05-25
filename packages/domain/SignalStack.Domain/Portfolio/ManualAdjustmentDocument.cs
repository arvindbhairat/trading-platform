using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Domain.Portfolio;

/// <summary>
/// Document model for the <c>manual_adjustments</c> MongoDB collection.
///
/// Records user-initiated manual adjustment entries tied to a specific holding
/// or position context (REQ-RECON-004). Adjustment records are audit-critical
/// and must never be deleted.
///
/// REQ-RECON-003: manual corrections are recorded as explicit adjustment entries,
/// not silent edits to trade-ledger history.
/// REQ-RECON-004: adjustment entries recorded against the affected holding/position.
/// </summary>
public sealed class ManualAdjustmentDocument
{
    [BsonId]
    public ObjectId Id { get; init; } = ObjectId.GenerateNewId();

    /// <summary>Platform user ID who owns the holding being adjusted.</summary>
    public required string UserId { get; init; }

    /// <summary>NSE trading symbol the adjustment applies to (e.g. "RELIANCE").</summary>
    public required string Symbol { get; init; }

    /// <summary>
    /// Adjustment type identifier.
    /// One of: "quantity", "cost_basis", "opening_balance".
    /// </summary>
    public required string AdjustmentType { get; init; }

    /// <summary>
    /// Quantity delta in shares. Positive for additions, negative for reductions.
    /// </summary>
    public int QuantityDelta { get; init; }

    /// <summary>
    /// Cost basis delta in INR. Positive for increases, negative for decreases.
    /// </summary>
    public decimal CostBasisDelta { get; init; }

    /// <summary>Human-readable reason for the adjustment.</summary>
    public required string Reason { get; init; }

    /// <summary>User ID of the user who requested the adjustment.</summary>
    public required string RequestedBy { get; init; }

    /// <summary>User ID of the admin who reviewed and approved the adjustment. Null for user-initiated seeding.</summary>
    public string? ApprovedBy { get; init; }

    /// <summary>The audit event ID linking this adjustment to the audit trail.</summary>
    public ObjectId? AuditEventId { get; init; }

    /// <summary>The trade_ledger trade_id that records the adjustment entry in the ledger.</summary>
    public string? LedgerTradeId { get; init; }

    /// <summary>UTC timestamp when this adjustment record was created.</summary>
    public required DateTime CreatedAt { get; init; }

    /// <summary>Optional notes for additional context.</summary>
    public string? Notes { get; init; }
}
