namespace SignalStack.Api.Fyers;

/// <summary>
/// Abstraction over the MongoDB <c>fyers_tokens</c> collection lifecycle.
/// REQ-AUTH-007: persistence with ownership, expiry, status, and dirty state.
/// </summary>
public interface IFyersTokenRepository
{
    /// <summary>
    /// Stores a new FYERS token for the user. If an active token already exists
    /// for this user, marks the prior token as <c>superseded</c> before inserting.
    /// REQ-AUTH-008: day-scoped tokens are replaced on each new OAuth flow.
    /// </summary>
    Task SaveTokenAsync(FyersTokenDocument token, CancellationToken ct = default);

    /// <summary>Returns the active token for the user, or <see langword="null"/> if none exists.</summary>
    Task<FyersTokenDocument?> FindActiveByUserIdAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns the dirty token for the user, or <see langword="null"/> if none.
    /// REQ-AUTH-009: dirty tokens are checked before sync workflows.
    /// </summary>
    Task<FyersTokenDocument?> FindDirtyByUserIdAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Marks the user's active token as <c>dirty</c>.
    /// REQ-AUTH-009: called when a sync/broker workflow fails due to invalid token.
    /// REQ-AUTH-010: dirty status blocks dependent sync workflows.
    /// </summary>
    Task MarkDirtyAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if the user has an active (non-dirty, non-expired) token.
    /// Returns <c>false</c> if the token is missing, dirty, or expired.
    /// </summary>
    Task<bool> HasActiveTokenAsync(string userId, CancellationToken ct = default);
}
