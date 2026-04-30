using System.Collections.Concurrent;
using SignalStack.Api.Sessions;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory <see cref="ISessionRepository"/> for use in integration tests that
/// do not have a live MongoDB connection.
/// Enforces the single-active-session invariant: <see cref="CreateSessionAsync"/>
/// removes all prior sessions for the user before inserting the new one.
/// </summary>
public sealed class InMemorySessionRepository : ISessionRepository
{
    // Keyed by session_token (jti).
    private readonly ConcurrentDictionary<string, SessionDocument> _byToken = new();
    // Keyed by user_id — stores the current active session_token for quick lookup.
    private readonly ConcurrentDictionary<string, string> _userToToken = new();

    public Task CreateSessionAsync(
        string userId,
        string sessionToken,
        DateTime issuedAt,
        DateTime expiresAt,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        // Atomically replace: remove old session for user, then add new one.
        if (_userToToken.TryRemove(userId, out var oldToken))
            _byToken.TryRemove(oldToken, out _);

        var doc = new SessionDocument
        {
            UserId = userId,
            SessionToken = sessionToken,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            UserAgent = userAgent,
        };

        _byToken[sessionToken] = doc;
        _userToToken[userId] = sessionToken;

        return Task.CompletedTask;
    }

    public Task<SessionDocument?> FindBySessionTokenAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        _byToken.TryGetValue(sessionToken, out var doc);
        return Task.FromResult<SessionDocument?>(doc);
    }

    public Task<SessionDocument?> FindByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        SessionDocument? doc = null;
        if (_userToToken.TryGetValue(userId, out var token))
            _byToken.TryGetValue(token, out doc);
        return Task.FromResult(doc);
    }
}
