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
    /// Records the step-up re-authentication timestamp on the session identified by
    /// <paramref name="sessionToken"/>.  REQ-SEC-011.
    /// </summary>
    Task UpdateStepUpAsync(
        string sessionToken,
        DateTime stepUpAuthenticatedAt,
        CancellationToken cancellationToken = default);
}
