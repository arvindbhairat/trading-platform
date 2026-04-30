using MongoDB.Bson;

namespace SignalStack.Api.Audit;

/// <summary>
/// Abstraction over the immutable <c>audit_events</c> collection.
/// REQ-SEC-011, REQ-CONFIG-005a.
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
}
