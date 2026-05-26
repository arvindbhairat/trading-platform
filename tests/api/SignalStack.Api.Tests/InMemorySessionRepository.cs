using System.Collections.Concurrent;
using MongoDB.Bson;
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
        DateTime? mfaVerifiedAt = null,
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
            MfaVerifiedAt = mfaVerifiedAt,
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

    public Task UpdateStepUpAsync(
        string sessionToken,
        DateTime stepUpAuthenticatedAt,
        ObjectId stepUpEventId,
        CancellationToken cancellationToken = default)
    {
        if (_byToken.TryGetValue(sessionToken, out var doc))
        {
            // SessionDocument uses { get; init; } — create a new instance with the
            // updated step_up_authenticated_at value.
            var updated = new SessionDocument
            {
                Id = doc.Id,
                UserId = doc.UserId,
                SessionToken = doc.SessionToken,
                IssuedAt = doc.IssuedAt,
                ExpiresAt = doc.ExpiresAt,
                UserAgent = doc.UserAgent,
                MfaVerifiedAt = doc.MfaVerifiedAt,
                StepUpAuthenticatedAt = stepUpAuthenticatedAt,
                StepUpEventId = stepUpEventId,
                ImpersonatingUserId = doc.ImpersonatingUserId,
                ImpersonationStartedAt = doc.ImpersonationStartedAt,
                ImpersonationLastActivityAt = doc.ImpersonationLastActivityAt,
            };
            _byToken[sessionToken] = updated;
        }
        return Task.CompletedTask;
    }

    // REQ-ADMIN-015: start impersonation
    public Task StartImpersonationAsync(
        string sessionToken,
        string targetUserId,
        DateTime startedAt,
        CancellationToken cancellationToken = default)
    {
        if (_byToken.TryGetValue(sessionToken, out var doc))
        {
            var updated = new SessionDocument
            {
                Id = doc.Id,
                UserId = doc.UserId,
                SessionToken = doc.SessionToken,
                IssuedAt = doc.IssuedAt,
                ExpiresAt = doc.ExpiresAt,
                UserAgent = doc.UserAgent,
                MfaVerifiedAt = doc.MfaVerifiedAt,
                StepUpAuthenticatedAt = doc.StepUpAuthenticatedAt,
                StepUpEventId = doc.StepUpEventId,
                ImpersonatingUserId = targetUserId,
                ImpersonationStartedAt = startedAt,
                ImpersonationLastActivityAt = startedAt,
            };
            _byToken[sessionToken] = updated;
        }
        return Task.CompletedTask;
    }

    // REQ-ADMIN-015: stop impersonation
    public Task StopImpersonationAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        if (_byToken.TryGetValue(sessionToken, out var doc))
        {
            var updated = new SessionDocument
            {
                Id = doc.Id,
                UserId = doc.UserId,
                SessionToken = doc.SessionToken,
                IssuedAt = doc.IssuedAt,
                ExpiresAt = doc.ExpiresAt,
                UserAgent = doc.UserAgent,
                MfaVerifiedAt = doc.MfaVerifiedAt,
                StepUpAuthenticatedAt = doc.StepUpAuthenticatedAt,
                StepUpEventId = doc.StepUpEventId,
            };
            _byToken[sessionToken] = updated;
        }
        return Task.CompletedTask;
    }

    // REQ-ADMIN-015: update activity timestamp
    public Task UpdateImpersonationActivityAsync(
        string sessionToken,
        DateTime activityAt,
        CancellationToken cancellationToken = default)
    {
        if (_byToken.TryGetValue(sessionToken, out var doc))
        {
            var updated = new SessionDocument
            {
                Id = doc.Id,
                UserId = doc.UserId,
                SessionToken = doc.SessionToken,
                IssuedAt = doc.IssuedAt,
                ExpiresAt = doc.ExpiresAt,
                UserAgent = doc.UserAgent,
                MfaVerifiedAt = doc.MfaVerifiedAt,
                StepUpAuthenticatedAt = doc.StepUpAuthenticatedAt,
                StepUpEventId = doc.StepUpEventId,
                ImpersonatingUserId = doc.ImpersonatingUserId,
                ImpersonationStartedAt = doc.ImpersonationStartedAt,
                ImpersonationLastActivityAt = activityAt,
            };
            _byToken[sessionToken] = updated;
        }
        return Task.CompletedTask;
    }

    // REQ-SESSION-004: remove the session from both dictionaries.
    public Task InvalidateSessionAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        if (_byToken.TryRemove(sessionToken, out var removed))
        {
            // Also clean up the user→token mapping so the user has no active session.
            _userToToken.TryRemove(removed.UserId, out _);
        }
        return Task.CompletedTask;
    }
}
