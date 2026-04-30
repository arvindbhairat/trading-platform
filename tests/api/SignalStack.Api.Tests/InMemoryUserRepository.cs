using System.Collections.Concurrent;
using MongoDB.Bson;
using SignalStack.Api.Users;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory <see cref="IUserRepository"/> for integration tests that do not
/// have a live MongoDB connection.
/// </summary>
public sealed class InMemoryUserRepository : IUserRepository
{
    private readonly ConcurrentDictionary<string, UserDocument> _byUserId = new();

    public Task<UserDocument?> FindByUserIdAsync(string userId, CancellationToken ct = default)
    {
        _byUserId.TryGetValue(userId, out var doc);
        return Task.FromResult<UserDocument?>(doc);
    }

    public Task UpsertOnSignInAsync(
        string userId, string email, string displayName, string provider,
        CancellationToken ct = default)
    {
        _byUserId.AddOrUpdate(
            userId,
            _ => new UserDocument
            {
                Id = ObjectId.GenerateNewId(),
                UserId = userId,
                Email = email,
                DisplayName = displayName,
                Provider = provider,
                Role = UserRole.User,
                Status = UserApprovalState.PendingApproval,
                LedgerSnapshotVersion = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.UpdatedAt = DateTime.UtcNow;
                return existing;
            });
        return Task.CompletedTask;
    }

    public Task<long> CountApprovedActiveAsync(CancellationToken ct = default)
    {
        var count = _byUserId.Values
            .Count(u => u.Status == UserApprovalState.Approved);
        return Task.FromResult((long)count);
    }

    public Task<bool> SetApprovedAsync(string userId, CancellationToken ct = default)
    {
        if (!_byUserId.TryGetValue(userId, out var doc)) return Task.FromResult(false);
        if (doc.Status == UserApprovalState.Deactivated) return Task.FromResult(false);
        doc.Status = UserApprovalState.Approved;
        doc.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task DeactivateAsync(string userId, CancellationToken ct = default)
    {
        if (_byUserId.TryGetValue(userId, out var doc))
        {
            doc.Status = UserApprovalState.Deactivated;
            doc.UpdatedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task UpsertAdminOnSignInAsync(
        string userId, string email, string displayName, string provider,
        CancellationToken ct = default)
    {
        _byUserId.AddOrUpdate(
            userId,
            _ => new UserDocument
            {
                Id = ObjectId.GenerateNewId(),
                UserId = userId,
                Email = email,
                DisplayName = displayName,
                Provider = provider,
                Role = UserRole.Admin,
                Status = UserApprovalState.Approved,
                LedgerSnapshotVersion = 1,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            (_, existing) =>
            {
                existing.Role = UserRole.Admin;
                existing.Status = UserApprovalState.Approved;
                existing.UpdatedAt = DateTime.UtcNow;
                return existing;
            });
        return Task.CompletedTask;
    }
}
