using SignalStack.Api.Fyers;
using SignalStack.Storage.Fyers;

namespace SignalStack.Api.Tests;

/// <summary>
/// Thread-safe in-memory implementation of <see cref="IFyersTokenRepository"/>
/// for use in integration tests without a live MongoDB connection.
/// </summary>
public sealed class InMemoryFyersTokenRepository : IFyersTokenRepository
{
    private readonly List<FyersTokenDocument> _tokens = [];
    private readonly object _lock = new();

    public Task SaveTokenAsync(FyersTokenDocument token, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // Supersede any existing active token for this user.
            foreach (var t in _tokens.Where(t => t.UserId == token.UserId && t.Status == FyersTokenStatus.Active))
            {
                t.Status = FyersTokenStatus.Superseded;
            }

            _tokens.Add(token);
        }

        return Task.CompletedTask;
    }

    public Task<FyersTokenDocument?> FindActiveByUserIdAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var token = _tokens.FirstOrDefault(t =>
                t.UserId == userId && t.Status == FyersTokenStatus.Active);
            return Task.FromResult(token);
        }
    }

    public Task<FyersTokenDocument?> FindDirtyByUserIdAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var token = _tokens.FirstOrDefault(t =>
                t.UserId == userId && t.Status == FyersTokenStatus.Dirty);
            return Task.FromResult(token);
        }
    }

    public Task MarkDirtyAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            foreach (var t in _tokens.Where(t =>
                t.UserId == userId && t.Status == FyersTokenStatus.Active))
            {
                t.Status = FyersTokenStatus.Dirty;
                t.UpdatedAt = DateTime.UtcNow;
            }
        }

        return Task.CompletedTask;
    }

    public Task<string?> GetAccessTokenAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            // In tests, use the access_token_ref directly as the token value.
            var token = _tokens.FirstOrDefault(t =>
                t.UserId == userId
                && t.Status == FyersTokenStatus.Active
                && t.ExpiresAt > DateTime.UtcNow);
            return Task.FromResult(token?.AccessTokenRef);
        }
    }

    public Task<bool> HasActiveTokenAsync(string userId, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var hasActive = _tokens.Any(t =>
                t.UserId == userId
                && t.Status == FyersTokenStatus.Active
                && t.ExpiresAt > DateTime.UtcNow);
            return Task.FromResult(hasActive);
        }
    }
}
