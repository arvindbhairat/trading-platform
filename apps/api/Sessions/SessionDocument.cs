using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Api.Sessions;

/// <summary>
/// Server-side session record stored in the MongoDB <c>sessions</c> collection.
/// REQ-SESSION-001/002/002a: 24 h TTL via index on <see cref="IssuedAt"/>; one
/// active session per user; <see cref="SessionToken"/> equals the JWT <c>jti</c>.
/// </summary>
public sealed class SessionDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    [BsonElement("user_id")]
    public required string UserId { get; init; }

    // Equals the JWT jti claim — used to validate authenticated API requests.
    [BsonElement("session_token")]
    public required string SessionToken { get; init; }

    // TTL index on this field expires documents 24 h after issuance.
    [BsonElement("issued_at")]
    public required DateTime IssuedAt { get; init; }

    [BsonElement("expires_at")]
    public required DateTime ExpiresAt { get; init; }

    [BsonElement("user_agent")]
    public string? UserAgent { get; init; }

    // REQ-BCP-009: set when the admin user's OAuth amr claim confirms MFA was used.
    // Null on sessions where MFA was not verified.
    [BsonElement("mfa_verified_at")]
    [BsonIgnoreIfNull]
    public DateTime? MfaVerifiedAt { get; init; }

    // REQ-SEC-011: set when the admin completes a step-up re-authentication.
    // Null on sessions where step-up has not been performed since session creation.
    // Checked by gated endpoints: if older than 5 minutes, step-up is required.
    [BsonElement("step_up_authenticated_at")]
    [BsonIgnoreIfNull]
    public DateTime? StepUpAuthenticatedAt { get; init; }

    // REQ-SEC-011 / REQ-LEGAL-001: the audit event _id of the most recent
    // step-up re-authentication for this session.  Used by gated endpoints to
    // chain their audit events back to the authorising step-up (RecordStepUpGatedAsync).
    [BsonElement("step_up_event_id")]
    [BsonIgnoreIfNull]
    public ObjectId? StepUpEventId { get; init; }

    // REQ-ADMIN-015: admin impersonation — when set, the admin is viewing the
    // portal as this target user.  Non-null means impersonation is active.
    [BsonElement("impersonating_user_id")]
    [BsonIgnoreIfNull]
    public string? ImpersonatingUserId { get; init; }

    // REQ-ADMIN-015: when impersonation started (UTC). Null when not impersonating.
    [BsonElement("impersonation_started_at")]
    [BsonIgnoreIfNull]
    public DateTime? ImpersonationStartedAt { get; init; }

    // REQ-ADMIN-015: last activity during impersonation (UTC), used for idle timeout.
    // Reset on each authenticated request while impersonating.
    [BsonElement("impersonation_last_activity_at")]
    [BsonIgnoreIfNull]
    public DateTime? ImpersonationLastActivityAt { get; init; }
}
