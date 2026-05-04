using MongoDB.Bson;

namespace SignalStack.Api.Sessions;

/// <summary>
/// Abstraction over the MongoDB <c>sessions</c> collection lifecycle.
/// REQ-SESSION-001/002/002a.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Atomically invalidates all prior sessions for <paramref name="userId"/> and
    /// inserts the new session record.  REQ-SESSION-002 / REQ-SESSION-002a.
    /// </summary>
    Task CreateSessionAsync(
        string userId,
        string sessionToken,
        DateTime issuedAt,
        DateTime expiresAt,
        string? userAgent,
        DateTime? mfaVerifiedAt = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the session whose token matches, or <see langword="null"/> if not found or TTL-expired.</summary>
    Task<SessionDocument?> FindBySessionTokenAsync(
        string sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the active session for the user, or <see langword="null"/> if none.</summary>
    Task<SessionDocument?> FindByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the step-up re-authentication timestamp and the corresponding
    /// audit event <c>_id</c> on the session identified by <paramref name="sessionToken"/>.
    /// REQ-SEC-011 / REQ-LEGAL-001.
    /// </summary>
    Task UpdateStepUpAsync(
        string sessionToken,
        DateTime stepUpAuthenticatedAt,
        ObjectId stepUpEventId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts impersonation for this session: stores the target user ID,
    /// start time, and initialises the activity timer.
    /// REQ-ADMIN-015.
    /// </summary>
    Task StartImpersonationAsync(
        string sessionToken,
        string targetUserId,
        DateTime startedAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops impersonation for this session: clears impersonation fields.
    /// REQ-ADMIN-015.
    /// </summary>
    Task StopImpersonationAsync(
        string sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last-activity timestamp for an active impersonation session.
    /// Called on every authenticated request during impersonation for idle-timeout tracking.
    /// REQ-ADMIN-015.
    /// </summary>
    Task UpdateImpersonationActivityAsync(
        string sessionToken,
        DateTime activityAt,
        CancellationToken cancellationToken = default);
}
