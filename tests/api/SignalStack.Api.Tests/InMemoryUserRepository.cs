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

    public Task<UserDocument?> FindByEmailAsync(string email, CancellationToken ct = default)
    {
        var doc = _byUserId.Values.FirstOrDefault(
            u => string.Equals(u.Email, email, StringComparison.OrdinalIgnoreCase));
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

    public Task<List<UserDocument>> ListUsersAsync(
        string? statusFilter = null, CancellationToken ct = default)
    {
        var users = _byUserId.Values
            .OrderByDescending(u => u.CreatedAt)
            .AsEnumerable();

        if (statusFilter is not null)
            users = users.Where(u => u.Status == statusFilter);

        return Task.FromResult(users.ToList());
    }

    public Task<long> CountPendingAsync(CancellationToken ct = default)
    {
        var count = _byUserId.Values
            .Count(u => u.Status == UserApprovalState.PendingApproval);
        return Task.FromResult((long)count);
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

    public Task<UserDocument?> FindByLinkedIdentityAsync(
        string provider, string providerKey, CancellationToken ct = default)
    {
        var doc = _byUserId.Values.FirstOrDefault(u =>
            u.LinkedIdentities?.Any(i =>
                i.Provider == provider && i.ProviderKey == providerKey) == true);
        return Task.FromResult<UserDocument?>(doc);
    }

    public Task<List<LinkedIdentity>> LinkIdentityAsync(
        string userId, string provider, string providerKey, string email,
        CancellationToken ct = default)
    {
        if (!_byUserId.TryGetValue(userId, out var doc))
            throw new InvalidOperationException("User not found.");

        // Check cross-user uniqueness.
        var otherOwner = _byUserId.Values.FirstOrDefault(u =>
            u.UserId != userId &&
            u.LinkedIdentities?.Any(i =>
                i.Provider == provider && i.ProviderKey == providerKey) == true);
        if (otherOwner is not null)
            throw new InvalidOperationException(
                "This identity is already linked to another account.");

        var linked = doc.LinkedIdentities?.ToList() ?? [];

        if (linked.Any(i => i.Provider == provider && i.ProviderKey == providerKey))
            return Task.FromResult(linked);

        if (linked.Count >= 2)
            throw new InvalidOperationException(
                "Maximum of 2 linked identities reached. Unlink one first.");

        var newIdentity = new LinkedIdentity
        {
            Provider = provider,
            ProviderKey = providerKey,
            Email = email,
            LinkedAt = DateTime.UtcNow,
        };
        linked.Add(newIdentity);

        _byUserId[userId] = CloneWith(doc, linkedIdentities: linked, updatedAt: DateTime.UtcNow);
        return Task.FromResult(linked);
    }

    public Task UnlinkIdentityAsync(
        string userId, string provider, string providerKey,
        CancellationToken ct = default)
    {
        if (_byUserId.TryGetValue(userId, out var doc) && doc.LinkedIdentities is not null)
        {
            var remaining = doc.LinkedIdentities
                .Where(i => !(i.Provider == provider && i.ProviderKey == providerKey))
                .ToList();
            _byUserId[userId] = CloneWith(doc, linkedIdentities: remaining, updatedAt: DateTime.UtcNow);
        }
        return Task.CompletedTask;
    }

    public Task UpdateLegalAcceptanceAsync(
        string userId,
        string? acceptedTosVersion,
        string? acceptedPrivacyVersion,
        string? acceptedTesterAcknowledgementVersion,
        bool acceptedMinorDeclaration,
        DateTime acceptedAt,
        CancellationToken ct = default)
    {
        if (_byUserId.TryGetValue(userId, out var doc))
        {
            doc.AcceptedTosVersion = acceptedTosVersion;
            doc.AcceptedPrivacyVersion = acceptedPrivacyVersion;
            doc.AcceptedTesterAcknowledgementVersion = acceptedTesterAcknowledgementVersion;
            doc.AcceptedMinorDeclaration = acceptedMinorDeclaration;
            doc.LegalAcceptedAt = acceptedAt;
            doc.UpdatedAt = acceptedAt;
        }
        return Task.CompletedTask;
    }

    // REQ-PRIVACY-004: erasure — redacts personal identifiers.
    public Task RedactPersonalDataAsync(
        string userId, string ticketId, DateTime redactedAt, CancellationToken ct = default)
    {
        if (_byUserId.TryGetValue(userId, out var doc))
        {
            // Replace the document in-place since critical fields are init-only.
            _byUserId[userId] = new UserDocument
            {
                Id = doc.Id,
                UserId = doc.UserId,
                Email = $"redacted-{ticketId}@dsar.local",
                DisplayName = $"[REDACTED PER {ticketId}]",
                Provider = "redacted",
                Role = doc.Role,
                Status = UserApprovalState.Deactivated,
                LedgerSnapshotVersion = doc.LedgerSnapshotVersion,
                LinkedIdentities = null,
                AcceptedTosVersion = doc.AcceptedTosVersion,
                AcceptedPrivacyVersion = doc.AcceptedPrivacyVersion,
                AcceptedTesterAcknowledgementVersion = doc.AcceptedTesterAcknowledgementVersion,
                AcceptedMinorDeclaration = doc.AcceptedMinorDeclaration,
                LegalAcceptedAt = doc.LegalAcceptedAt,
                CreatedAt = doc.CreatedAt,
                UpdatedAt = redactedAt,
            };
        }
        return Task.CompletedTask;
    }

    public Task<UserDocument> RebindIdentityAsync(
        string targetUserId, string newProvider, string newProviderKey, string newEmail,
        CancellationToken ct = default)
    {
        if (!_byUserId.TryGetValue(targetUserId, out var doc))
            throw new InvalidOperationException("Target user not found.");

        var newUserId = $"{newProvider}:{newProviderKey}";

        var updated = new UserDocument
        {
            Id = doc.Id,
            UserId = newUserId,
            Email = newEmail,
            DisplayName = doc.DisplayName,
            Provider = newProvider,
            Role = doc.Role,
            Status = doc.Status,
            LedgerSnapshotVersion = doc.LedgerSnapshotVersion,
            EquityBaseOverride = doc.EquityBaseOverride,
            EquityBaseOverrideUpdatedAt = doc.EquityBaseOverrideUpdatedAt,
            LinkedIdentities = null,
            CreatedAt = doc.CreatedAt,
            UpdatedAt = DateTime.UtcNow,
        };

        _byUserId.TryRemove(targetUserId, out _);
        _byUserId[newUserId] = updated;

        return Task.FromResult(updated);
    }

    public Task SetSignalsSuspendedAsync(
        string userId, bool suspended, CancellationToken ct = default)
    {
        if (_byUserId.TryGetValue(userId, out var doc))
        {
            doc.SignalsSuspended = suspended;
            doc.UpdatedAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    public Task SetEquityOverrideAsync(
        string userId, decimal? overrideValue, CancellationToken ct = default)
    {
        if (_byUserId.TryGetValue(userId, out var doc))
        {
            doc.EquityBaseOverride = overrideValue;
            doc.EquityBaseOverrideUpdatedAt = overrideValue.HasValue ? DateTime.UtcNow : null;
        }
        return Task.CompletedTask;
    }

    private static UserDocument CloneWith(
        UserDocument source,
        List<LinkedIdentity>? linkedIdentities = null,
        DateTime? updatedAt = null)
    {
        return new UserDocument
        {
            Id = source.Id,
            UserId = source.UserId,
            Email = source.Email,
            DisplayName = source.DisplayName,
            Provider = source.Provider,
            Role = source.Role,
            Status = source.Status,
            LedgerSnapshotVersion = source.LedgerSnapshotVersion,
            EquityBaseOverride = source.EquityBaseOverride,
            EquityBaseOverrideUpdatedAt = source.EquityBaseOverrideUpdatedAt,
            LinkedIdentities = linkedIdentities ?? source.LinkedIdentities,
            CreatedAt = source.CreatedAt,
            UpdatedAt = updatedAt ?? source.UpdatedAt,
        };
    }
}
