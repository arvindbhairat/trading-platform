using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Users;

namespace SignalStack.Api.Users;

public sealed class MongoUserRepository : IUserRepository
{
    private readonly IMongoCollection<UserDocument> _users;

    public MongoUserRepository(IMongoDatabase database)
    {
        _users = database.GetCollection<UserDocument>("users");
    }

    public async Task<UserDocument?> FindByUserIdAsync(
        string userId,
        CancellationToken ct = default)
    {
        return await _users
            .Find(Builders<UserDocument>.Filter.Eq(u => u.UserId, userId))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<UserDocument?> FindByEmailAsync(
        string email,
        CancellationToken ct = default)
    {
        return await _users
            .Find(Builders<UserDocument>.Filter.Eq(u => u.Email, email))
            .FirstOrDefaultAsync(ct);
    }

    // REQ-RECOVERY-001: searches the linked_identities array across all users.
    public async Task<UserDocument?> FindByLinkedIdentityAsync(
        string provider, string providerKey, CancellationToken ct = default)
    {
        var filter = Builders<UserDocument>.Filter.ElemMatch(
            u => u.LinkedIdentities,
            Builders<LinkedIdentity>.Filter.And(
                Builders<LinkedIdentity>.Filter.Eq(i => i.Provider, provider),
                Builders<LinkedIdentity>.Filter.Eq(i => i.ProviderKey, providerKey)));

        return await _users.Find(filter).FirstOrDefaultAsync(ct);
    }

    // Upsert: insert with pending_approval on first sign-in; update display_name + updated_at
    // on subsequent sign-ins without touching role or status.
    public async Task UpsertOnSignInAsync(
        string userId,
        string email,
        string displayName,
        string provider,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);

        var setOnInsert = Builders<UserDocument>.Update
            .SetOnInsert(u => u.Id, ObjectId.GenerateNewId())
            .SetOnInsert(u => u.UserId, userId)
            .SetOnInsert(u => u.Email, email)
            .SetOnInsert(u => u.Provider, provider)
            .SetOnInsert(u => u.Role, UserRole.User)
            .SetOnInsert(u => u.Status, UserApprovalState.PendingApproval)
            .SetOnInsert(u => u.LedgerSnapshotVersion, 1L)
            .SetOnInsert(u => u.CreatedAt, now);

        // Always update display_name and updated_at on every sign-in.
        var setAlways = Builders<UserDocument>.Update
            .Set(u => u.DisplayName, displayName)
            .Set(u => u.UpdatedAt, now);

        var combined = Builders<UserDocument>.Update.Combine(setOnInsert, setAlways);

        await _users.UpdateOneAsync(filter, combined,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task<long> CountApprovedActiveAsync(CancellationToken ct = default)
    {
        return await _users.CountDocumentsAsync(
            Builders<UserDocument>.Filter.Eq(u => u.Status, UserApprovalState.Approved),
            cancellationToken: ct);
    }

    public async Task<bool> SetApprovedAsync(string userId, CancellationToken ct = default)
    {
        var filter = Builders<UserDocument>.Filter.And(
            Builders<UserDocument>.Filter.Eq(u => u.UserId, userId),
            Builders<UserDocument>.Filter.Ne(u => u.Status, UserApprovalState.Deactivated));

        var update = Builders<UserDocument>.Update
            .Set(u => u.Status, UserApprovalState.Approved)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        var result = await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task DeactivateAsync(string userId, CancellationToken ct = default)
    {
        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);
        var update = Builders<UserDocument>.Update
            .Set(u => u.Status, UserApprovalState.Deactivated)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    // REQ-ADMIN-001b: reactivates a deactivated user — only works for deactivated users.
    public async Task<bool> ReactivateAsync(string userId, CancellationToken ct = default)
    {
        var filter = Builders<UserDocument>.Filter.And(
            Builders<UserDocument>.Filter.Eq(u => u.UserId, userId),
            Builders<UserDocument>.Filter.Eq(u => u.Status, UserApprovalState.Deactivated));

        var update = Builders<UserDocument>.Update
            .Set(u => u.Status, UserApprovalState.Approved)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        var result = await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<List<UserDocument>> ListUsersAsync(
        string? statusFilter = null, CancellationToken ct = default)
    {
        var filter = statusFilter is not null
            ? Builders<UserDocument>.Filter.Eq(u => u.Status, statusFilter)
            : Builders<UserDocument>.Filter.Empty;

        return await _users
            .Find(filter)
            .Sort(Builders<UserDocument>.Sort.Descending(u => u.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<long> CountPendingAsync(CancellationToken ct = default)
    {
        return await _users.CountDocumentsAsync(
            Builders<UserDocument>.Filter.Eq(u => u.Status, UserApprovalState.PendingApproval),
            cancellationToken: ct);
    }

    // REQ-LEGAL-004/008, REQ-PRIVACY-003/007: records legal document acceptance.
    public async Task UpdateLegalAcceptanceAsync(
        string userId,
        string? acceptedTosVersion,
        string? acceptedPrivacyVersion,
        string? acceptedTesterAcknowledgementVersion,
        bool acceptedMinorDeclaration,
        DateTime acceptedAt,
        CancellationToken ct = default)
    {
        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);
        var update = Builders<UserDocument>.Update
            .Set(u => u.AcceptedTosVersion, acceptedTosVersion)
            .Set(u => u.AcceptedPrivacyVersion, acceptedPrivacyVersion)
            .Set(u => u.AcceptedTesterAcknowledgementVersion, acceptedTesterAcknowledgementVersion)
            .Set(u => u.AcceptedMinorDeclaration, acceptedMinorDeclaration)
            .Set(u => u.LegalAcceptedAt, acceptedAt)
            .Set(u => u.UpdatedAt, acceptedAt);

        await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    // REQ-ROLE-005: atomically creates or promotes the user to admin + approved.
    public async Task UpsertAdminOnSignInAsync(
        string userId,
        string email,
        string displayName,
        string provider,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);

        var setOnInsert = Builders<UserDocument>.Update
            .SetOnInsert(u => u.Id, ObjectId.GenerateNewId())
            .SetOnInsert(u => u.UserId, userId)
            .SetOnInsert(u => u.Email, email)
            .SetOnInsert(u => u.Provider, provider)
            .SetOnInsert(u => u.LedgerSnapshotVersion, 1L)
            .SetOnInsert(u => u.CreatedAt, now);

        // Always set role and status on every admin sign-in so the bootstrap is
        // idempotent — $set handles both insert and update during upsert, avoiding
        // a conflict with $setOnInsert on the same fields.
        var setAlways = Builders<UserDocument>.Update
            .Set(u => u.DisplayName, displayName)
            .Set(u => u.Role, UserRole.Admin)
            .Set(u => u.Status, UserApprovalState.Approved)
            .Set(u => u.UpdatedAt, now);

        var combined = Builders<UserDocument>.Update.Combine(setOnInsert, setAlways);

        await _users.UpdateOneAsync(filter, combined,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    // REQ-RECOVERY-001: links a secondary OAuth identity, enforcing the max-2 limit.
    public async Task SetEquityOverrideAsync(
        string userId, decimal? overrideValue, CancellationToken ct = default)
    {
        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);
        var now = DateTime.UtcNow;

        if (overrideValue.HasValue)
        {
            var update = Builders<UserDocument>.Update
                .Set(u => u.EquityBaseOverride, overrideValue.Value)
                .Set(u => u.EquityBaseOverrideUpdatedAt, now)
                .Set(u => u.UpdatedAt, now);

            await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
        else
        {
            var update = Builders<UserDocument>.Update
                .Unset(u => u.EquityBaseOverride)
                .Unset(u => u.EquityBaseOverrideUpdatedAt)
                .Set(u => u.UpdatedAt, now);

            await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
        }
    }

    // REQ-ADMIN-010: sets or clears per-user signal suspension.
    public async Task SetSignalsSuspendedAsync(
        string userId, bool suspended, CancellationToken ct = default)
    {
        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);
        var update = Builders<UserDocument>.Update
            .Set(u => u.SignalsSuspended, suspended)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task<List<LinkedIdentity>> LinkIdentityAsync(
        string userId, string provider, string providerKey, string email,
        CancellationToken ct = default)
    {
        // Ensure no other user has this identity linked (uniqueness across all users).
        var existingOwner = await FindByLinkedIdentityAsync(provider, providerKey, ct);
        if (existingOwner is not null && existingOwner.UserId != userId)
        {
            throw new InvalidOperationException(
                "This identity is already linked to another account.");
        }

        // Read current linked identities to enforce limits.
        var user = await FindByUserIdAsync(userId, ct)
            ?? throw new InvalidOperationException("User not found.");

        var linked = user.LinkedIdentities ?? [];

        // Duplicate within the same user is a no-op.
        if (linked.Any(i => i.Provider == provider && i.ProviderKey == providerKey))
        {
            return linked;
        }

        // REQ-RECOVERY-002: max 2 linked identities.
        if (linked.Count >= 2)
        {
            throw new InvalidOperationException(
                "Maximum of 2 linked identities reached. Unlink one first.");
        }

        var now = DateTime.UtcNow;
        var newIdentity = new LinkedIdentity
        {
            Provider = provider,
            ProviderKey = providerKey,
            Email = email,
            LinkedAt = now,
        };

        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);
        var update = Builders<UserDocument>.Update
            .Push(u => u.LinkedIdentities, newIdentity)
            .Set(u => u.UpdatedAt, now);

        await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
        return [.. linked, newIdentity];
    }

    // REQ-RECOVERY-002: unlinks a secondary OAuth identity.
    public async Task UnlinkIdentityAsync(
        string userId, string provider, string providerKey,
        CancellationToken ct = default)
    {
        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);
        var update = Builders<UserDocument>.Update
            .PullFilter(u => u.LinkedIdentities,
                Builders<LinkedIdentity>.Filter.And(
                    Builders<LinkedIdentity>.Filter.Eq(i => i.Provider, provider),
                    Builders<LinkedIdentity>.Filter.Eq(i => i.ProviderKey, providerKey)))
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    // REQ-PRIVACY-004: erasure — redacts personal identifiers.
    public async Task RedactPersonalDataAsync(
        string userId, string ticketId, DateTime redactedAt, CancellationToken ct = default)
    {
        var redactedEmail = $"redacted-{ticketId}@dsar.local";
        var redactedName = $"[REDACTED PER {ticketId}]";

        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, userId);
        var update = Builders<UserDocument>.Update
            .Set(u => u.Status, UserApprovalState.Deactivated)
            .Set(u => u.Email, redactedEmail)
            .Set(u => u.DisplayName, redactedName)
            .Set(u => u.Provider, "redacted")
            .Set(u => u.LinkedIdentities, null)
            .Set(u => u.EquityBaseOverride, null)
            .Set(u => u.EquityBaseOverrideUpdatedAt, null)
            .Set(u => u.UpdatedAt, redactedAt);

        // Preserve: CreatedAt, Role, LedgerSnapshotVersion,
        // AcceptedTosVersion, AcceptedPrivacyVersion,
        // AcceptedTesterAcknowledgementVersion, AcceptedMinorDeclaration,
        // LegalAcceptedAt — these are retained as non-personal audit/consent facts.

        await _users.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    // REQ-RECOVERY-005: admin-assisted rebind — changes the primary identity.
    public async Task<UserDocument> RebindIdentityAsync(
        string targetUserId, string newProvider, string newProviderKey, string newEmail,
        CancellationToken ct = default)
    {
        var newUserId = $"{newProvider}:{newProviderKey}";
        var now = DateTime.UtcNow;

        var filter = Builders<UserDocument>.Filter.Eq(u => u.UserId, targetUserId);
        var update = Builders<UserDocument>.Update
            .Set(u => u.UserId, newUserId)
            .Set(u => u.Provider, newProvider)
            .Set(u => u.Email, newEmail)
            .Set(u => u.LinkedIdentities, null)
            .Set(u => u.UpdatedAt, now);

        var options = new FindOneAndUpdateOptions<UserDocument>
        {
            ReturnDocument = ReturnDocument.After,
        };

        var updated = await _users.FindOneAndUpdateAsync(filter, update, options, ct)
            ?? throw new InvalidOperationException("Target user not found.");
        return updated;
    }
}
