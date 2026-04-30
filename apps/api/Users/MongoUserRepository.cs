using MongoDB.Bson;
using MongoDB.Driver;

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
            .SetOnInsert(u => u.Role, UserRole.Admin)
            .SetOnInsert(u => u.Status, UserApprovalState.Approved)
            .SetOnInsert(u => u.LedgerSnapshotVersion, 1L)
            .SetOnInsert(u => u.CreatedAt, now);

        // Always elevate role and status on every admin sign-in so the bootstrap is
        // idempotent — subsequent sign-ins keep the admin role and approved status.
        var setAlways = Builders<UserDocument>.Update
            .Set(u => u.DisplayName, displayName)
            .Set(u => u.Role, UserRole.Admin)
            .Set(u => u.Status, UserApprovalState.Approved)
            .Set(u => u.UpdatedAt, now);

        var combined = Builders<UserDocument>.Update.Combine(setOnInsert, setAlways);

        await _users.UpdateOneAsync(filter, combined,
            new UpdateOptions { IsUpsert = true }, ct);
    }
}
