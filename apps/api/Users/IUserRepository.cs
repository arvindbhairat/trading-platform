namespace SignalStack.Api.Users;

public interface IUserRepository
{
    Task<UserDocument?> FindByUserIdAsync(string userId, CancellationToken ct = default);

    /// <summary>Returns the user whose email matches, or <see langword="null"/> if not found.</summary>
    Task<UserDocument?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>
    /// REQ-RECOVERY-001: finds the user who has the given (provider, providerKey)
    /// in their <see cref="UserDocument.LinkedIdentities"/> list.
    /// Returns <see langword="null"/> if no user has this identity linked.
    /// </summary>
    Task<UserDocument?> FindByLinkedIdentityAsync(
        string provider, string providerKey, CancellationToken ct = default);

    // Creates the user record as pending_approval if it does not exist yet.
    // On subsequent sign-ins for the same user_id, updates display_name and updated_at only;
    // never downgrades an already-approved or deactivated record.
    Task UpsertOnSignInAsync(
        string userId,
        string email,
        string displayName,
        string provider,
        CancellationToken ct = default);

    // REQ-ROLE-005: atomically creates or promotes a user to admin role with approved
    // status.  Used when the authenticated email matches SEED_ADMIN_EMAIL.
    // On insert: creates with role=admin, status=approved.
    // On update: sets role=admin, status=approved regardless of prior status
    // (deactivated admin re-activates on matching seed email sign-in).
    Task UpsertAdminOnSignInAsync(
        string userId,
        string email,
        string displayName,
        string provider,
        CancellationToken ct = default);

    // Returns the count of users with status = "approved" (non-deactivated).
    // Used by UserApprovalService to enforce REQ-LEGAL-003 ceiling.
    Task<long> CountApprovedActiveAsync(CancellationToken ct = default);

    // Transitions status from pending_approval → approved.
    // Returns false without writing if the user is not found or is already deactivated.
    Task<bool> SetApprovedAsync(string userId, CancellationToken ct = default);

    // Soft-deletes: transitions any status → deactivated.
    Task DeactivateAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns all users, optionally filtered by status (pending_approval / approved / deactivated).
    /// Ordered by createdAt descending.  Used by the admin user management page (P2-T12).
    /// </summary>
    Task<List<UserDocument>> ListUsersAsync(string? statusFilter = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the count of users with status = "pending_approval".
    /// Used by the admin navigation badge to show a live pending-approval count (P2-T12).
    /// </summary>
    Task<long> CountPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// REQ-RECOVERY-001: links a secondary OAuth identity to the user's account.
    /// Throws <see cref="InvalidOperationException"/> if the identity is already linked
    /// to a different user, or if the user already has 2 linked identities.
    /// Returns the updated linked identity list.
    /// </summary>
    Task<List<LinkedIdentity>> LinkIdentityAsync(
        string userId, string provider, string providerKey, string email,
        CancellationToken ct = default);

    /// <summary>
    /// REQ-RECOVERY-002: unlinks a secondary OAuth identity.  Throws
    /// <see cref="InvalidOperationException"/> if the provider is the user's
    /// primary provider (the one in the UserId prefix).
    /// </summary>
    Task UnlinkIdentityAsync(
        string userId, string provider, string providerKey,
        CancellationToken ct = default);

    /// <summary>
    /// REQ-RECOVERY-005: admin-assisted rebind — replaces all linked identities
    /// with a single new primary identity and clears the old provider/key.
    /// Only the OAuth identity binding changes; all other state is preserved.
    /// Returns the updated user document.
    /// </summary>
    Task<UserDocument> RebindIdentityAsync(
        string targetUserId,
        string newProvider,
        string newProviderKey,
        string newEmail,
        CancellationToken ct = default);
}
