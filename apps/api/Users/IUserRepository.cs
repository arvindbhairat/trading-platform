namespace SignalStack.Api.Users;

public interface IUserRepository
{
    Task<UserDocument?> FindByUserIdAsync(string userId, CancellationToken ct = default);

    // Creates the user record as pending_approval if it does not exist yet.
    // On subsequent sign-ins for the same user_id, updates display_name and updated_at only;
    // never downgrades an already-approved or deactivated record.
    Task UpsertOnSignInAsync(
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
}
