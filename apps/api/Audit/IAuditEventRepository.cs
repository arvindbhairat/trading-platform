using MongoDB.Bson;

namespace SignalStack.Api.Audit;

/// <summary>Filter for querying the audit event log (P8-T1 / admin helper APIs).</summary>
public sealed record AuditEventFilter(
    string? ActorId = null,
    string? ActionType = null,
    DateTime? From = null,
    DateTime? To = null);

/// <summary>
/// Abstraction over the immutable <c>audit_events</c> collection.
/// REQ-SEC-011, REQ-CONFIG-005a, P8-T1.
/// </summary>
public interface IAuditEventRepository
{
    /// <summary>
    /// Writes an immutable audit event record.  Returns the generated <see cref="AuditEventDocument.Id"/>
    /// so callers can reference it in subsequent audit rows (step-up chain, REQ-SEC-011).
    /// </summary>
    Task<ObjectId> RecordAsync(
        string actorId,
        string actionType,
        DateTime eventAt,
        Dictionary<string, object?>? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes an audit event that is gated by a step-up re-authentication, linking
    /// this event back to the step-up audit record (<c>stepUpEventId</c>).
    /// REQ-SEC-011 / REQ-CONFIG-005a.
    /// </summary>
    Task<ObjectId> RecordStepUpGatedAsync(
        string actorId,
        string actionType,
        DateTime eventAt,
        ObjectId stepUpEventId,
        Dictionary<string, object?>? details = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> if the given actor has at least one audit event matching
    /// the specified <paramref name="actionType"/>. Used by REQ-ROLE-007a to check
    /// whether the transfer recovery summary has been dismissed.
    /// </summary>
    Task<bool> HasEventAsync(
        string actorId,
        string actionType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Paginated list of audit events with optional filtering.
    /// P8-T1 admin helper API.
    /// </summary>
    Task<(IReadOnlyList<AuditEventDocument> Items, long TotalCount)> ListAsync(
        AuditEventFilter? filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a single audit event by its ObjectId, or null if not found.
    /// P8-T1 admin helper API.
    /// </summary>
    Task<AuditEventDocument?> GetByIdAsync(
        ObjectId id,
        CancellationToken cancellationToken = default);
}
