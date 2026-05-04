using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Worker.Rme;

/// <summary>
/// MongoDB document model for the <c>positions</c> collection.
///
/// Every position document carries a <c>_version</c> field (long, starting at 1,
/// incremented on every write) for Optimistic Concurrency Control per REQ-RME-CONC-002.
///
/// The <c>ledger_fence_token</c> field provides stale-write detection for the
/// fencing-token guard (REQ-PORT-031b(b)).
/// </summary>
public sealed class PositionDocument
{
    [BsonId]
    public Guid PositionId { get; init; }

    /// <summary>OCC version — starts at 1, incremented on every write. REQ-RME-CONC-002.</summary>
    [BsonElement("_version")]
    public long Version { get; set; } = 1;

    [BsonElement("user_id")]
    public required string UserId { get; init; }

    [BsonElement("symbol")]
    public required string Symbol { get; init; }

    /// <summary>Current lifecycle state. REQ-PLC-002.</summary>
    [BsonElement("state")]
    public PositionState State { get; set; } = PositionState.PendingEntry;

    [BsonElement("entry_price")]
    public decimal EntryPrice { get; set; }

    [BsonElement("current_price")]
    public decimal CurrentPrice { get; set; }

    [BsonElement("quantity")]
    public decimal Quantity { get; set; }

    [BsonElement("stop_loss")]
    public decimal StopLoss { get; set; }

    [BsonElement("trailing_stop")]
    public decimal TrailingStop { get; set; }

    // ── Advisory flags (REQ-PLC-002) ──────────────────────────────────────────

    [BsonElement("add_advisory_active")]
    public bool AddAdvisoryActive { get; set; }

    [BsonElement("reduce_advisory_active")]
    public bool ReduceAdvisoryActive { get; set; }

    [BsonElement("trailing_stop_active")]
    public bool TrailingStopActive { get; set; }

    [BsonElement("exit_advisory_active")]
    public bool ExitAdvisoryActive { get; set; }

    // ── Fencing token (REQ-PORT-031b) ─────────────────────────────────────────

    /// <summary>
    /// Fencing token from the lock acquisition protecting this write.
    /// Every position write includes the <c>ledger_fence_token</c> filter for
    /// stale-write detection (REQ-PORT-031b(b)).
    /// </summary>
    [BsonElement("ledger_fence_token")]
    public long LedgerFenceToken { get; set; }

    // ── Timestamps ────────────────────────────────────────────────────────────

    // ── Pyramiding / Tranche tracking (REQ-PYR-003, REQ-PYR-011) ──────────────

    /// <summary>
    /// Ordered list of pyramid tranches. The first entry is the initial position;
    /// subsequent entries are add-on tranches. Each tranche records its own
    /// entry price, quantity, and assigned stop loss at entry time.
    /// REQ-PYR-003, REQ-PYR-011.
    /// </summary>
    [BsonElement("tranches")]
    [BsonIgnoreIfDefault]
    public List<TrancheRecord> Tranches { get; set; } = [];

    /// <summary>
    /// Quantity-weighted average entry price across all tranches.
    /// Recalculated after each add or reduce (REQ-PYR-010).
    /// </summary>
    [BsonElement("average_entry_price")]
    public decimal AverageEntryPrice { get; set; }

    /// <summary>
    /// Number of pyramid (add-on) entries currently active.
    /// 0 = initial entry only, 1 = first add-on completed, etc.
    /// REQ-PYR-003: must not exceed risk.pyramiding.max_addon_entries.
    /// </summary>
    [BsonElement("pyramid_entry_count")]
    public int PyramidEntryCount { get; set; }

    /// <summary>
    /// The highest individual tranche stop loss currently in effect.
    /// Used as the floor for the never-lowers-stop invariant (REQ-PYR-011).
    /// When a new tranche is added and the recalculated combined stop is lower
    /// than this value, this value is preserved as the effective stop.
    /// </summary>
    [BsonElement("stop_floor")]
    public decimal StopFloor { get; set; }

    // ── Time Stop (REQ-STOP-003) ──────────────────────────────────────────────

    /// <summary>
    /// Time stop date computed at entry by adding the configured number of trading
    /// sessions to the entry date using the NSE trading calendar.
    /// Format: "yyyy-MM-dd". Null when the position does not use a Time Stop.
    /// REQ-STOP-003.
    /// </summary>
    [BsonElement("time_stop_date")]
    [BsonIgnoreIfNull]
    public string? TimeStopDate { get; set; }

    /// <summary>
    /// Audit trail for time_stop_date recomputations triggered by calendar edits
    /// (REQ-STOP-003a). Each entry records the previous date, new date, and the
    /// triggering event. Appended, never mutated in place.
    /// </summary>
    [BsonElement("time_stop_audit")]
    [BsonIgnoreIfDefault]
    public List<TimeStopAuditEntry> TimeStopAudit { get; set; } = [];

    // ── Timestamps ────────────────────────────────────────────────────────────

    [BsonElement("created_at")]
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("closed_at")]
    [BsonIgnoreIfNull]
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// Creates a shallow copy with updated state. Used by the RME pure-function
    /// consumer to produce the new position document without mutating the original.
    /// </summary>
    public PositionDocument CloneWith(
        PositionState? state = null,
        decimal? currentPrice = null,
        decimal? stopLoss = null,
        decimal? trailingStop = null,
        bool? addAdvisoryActive = null,
        bool? reduceAdvisoryActive = null,
        bool? trailingStopActive = null,
        bool? exitAdvisoryActive = null,
        long? ledgerFenceToken = null,
        string? timeStopDate = null,
        List<TimeStopAuditEntry>? timeStopAudit = null,
        List<TrancheRecord>? tranches = null,
        decimal? averageEntryPrice = null,
        int? pyramidEntryCount = null,
        decimal? stopFloor = null)
    {
        return new PositionDocument
        {
            PositionId = PositionId,
            Version = Version,
            UserId = UserId,
            Symbol = Symbol,
            State = state ?? State,
            EntryPrice = EntryPrice,
            CurrentPrice = currentPrice ?? CurrentPrice,
            Quantity = Quantity,
            StopLoss = stopLoss ?? StopLoss,
            TrailingStop = trailingStop ?? TrailingStop,
            AddAdvisoryActive = addAdvisoryActive ?? AddAdvisoryActive,
            ReduceAdvisoryActive = reduceAdvisoryActive ?? ReduceAdvisoryActive,
            TrailingStopActive = trailingStopActive ?? TrailingStopActive,
            ExitAdvisoryActive = exitAdvisoryActive ?? ExitAdvisoryActive,
            LedgerFenceToken = ledgerFenceToken ?? LedgerFenceToken,
            TimeStopDate = timeStopDate ?? TimeStopDate,
            TimeStopAudit = timeStopAudit ?? TimeStopAudit,
            Tranches = tranches ?? Tranches,
            AverageEntryPrice = averageEntryPrice ?? AverageEntryPrice,
            PyramidEntryCount = pyramidEntryCount ?? PyramidEntryCount,
            StopFloor = stopFloor ?? StopFloor,
            CreatedAt = CreatedAt,
            UpdatedAt = DateTime.UtcNow,
            ClosedAt = state is PositionState.Closed or PositionState.Rejected
                ? DateTime.UtcNow
                : ClosedAt,
        };
    }
}

/// <summary>
/// A single pyramid tranche within a position. The first entry is the initial
/// position; subsequent entries are add-on tranches. REQ-PYR-003, REQ-PYR-011.
/// </summary>
public sealed record TrancheRecord
{
    [BsonElement("entry_price")]
    public required decimal EntryPrice { get; init; }

    [BsonElement("quantity")]
    public required decimal Quantity { get; init; }

    [BsonElement("stop_loss")]
    public required decimal StopLoss { get; init; }

    [BsonElement("entered_at")]
    public required DateTime EnteredAt { get; init; }

    /// <summary>Position state at the time this tranche was active (Open or Closed).</summary>
    [BsonElement("state")]
    [BsonIgnoreIfNull]
    public string? State { get; init; }
}

/// <summary>
/// Audit entry for a time_stop_date recomputation triggered by a calendar edit.
/// REQ-STOP-003a.
/// </summary>
public sealed record TimeStopAuditEntry
{
    [BsonElement("previous_date")]
    public required string PreviousDate { get; init; }

    [BsonElement("new_date")]
    public required string NewDate { get; init; }

    /// <summary>
    /// The triggering event (e.g. "calendar_edit").
    /// </summary>
    [BsonElement("trigger_event")]
    public required string TriggerEvent { get; init; }

    [BsonElement("recomputed_at")]
    public required DateTime RecomputedAt { get; init; }
}
