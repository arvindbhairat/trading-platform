using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Domain.Audit;

/// <summary>
/// Immutable audit event record stored in the MongoDB <c>audit_events</c> collection.
/// REQ-SEC-011: step-up events are recorded here and chained to gated actions.
/// REQ-CONFIG-005a: sys_config mutations record prior/new values and the step-up ref.
/// </summary>
public sealed class AuditEventDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>The user ID (<c>provider:providerKey</c>) of the actor.</summary>
    [BsonElement("actor_id")]
    public required string ActorId { get; init; }

    /// <summary>Machine-readable action type, e.g. <c>step_up_authenticated</c>.</summary>
    [BsonElement("action_type")]
    public required string ActionType { get; init; }

    /// <summary>ISO 8601 UTC timestamp of the event.</summary>
    [BsonElement("event_at")]
    public required DateTime EventAt { get; init; }

    /// <summary>Free-form details payload (prior value, new value, request context, …).</summary>
    [BsonElement("details")]
    [BsonIgnoreIfNull]
    public Dictionary<string, object?>? Details { get; init; }

    /// <summary>
    /// When this audit event is gated by a step-up re-authentication
    /// (REQ-SEC-011 / REQ-CONFIG-005a), this field references the
    /// <c>audit_events._id</c> of the step-up authentication event that
    /// authorised the action.  Null when no step-up was required.
    /// </summary>
    [BsonElement("step_up_event_ref")]
    [BsonIgnoreIfNull]
    public ObjectId? StepUpEventRef { get; init; }
}
