using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Storage.Fyers;

public sealed class MongoFyersTokenRepository : IFyersTokenRepository
{
    private readonly IMongoCollection<FyersTokenDocument> _tokens;

    public MongoFyersTokenRepository(IMongoDatabase database)
    {
        _tokens = database.GetCollection<FyersTokenDocument>("fyers_tokens");
    }

    // REQ-AUTH-007/008: supersedes prior active token, stores the new one.
    public async Task SaveTokenAsync(
        FyersTokenDocument token,
        CancellationToken ct = default)
    {
        // Mark any existing active token as superseded.
        var activeFilter = Builders<FyersTokenDocument>.Filter.And(
            Builders<FyersTokenDocument>.Filter.Eq(t => t.UserId, token.UserId),
            Builders<FyersTokenDocument>.Filter.Eq(t => t.Status, FyersTokenStatus.Active));

        var supersedeUpdate = Builders<FyersTokenDocument>.Update
            .Set(t => t.Status, FyersTokenStatus.Superseded)
            .Set(t => t.SupersededAt, DateTime.UtcNow);

        await _tokens.UpdateManyAsync(activeFilter, supersedeUpdate, cancellationToken: ct);

        // Insert the new token.
        await _tokens.InsertOneAsync(token, cancellationToken: ct);
    }

    public async Task<FyersTokenDocument?> FindActiveByUserIdAsync(
        string userId,
        CancellationToken ct = default)
    {
        var filter = Builders<FyersTokenDocument>.Filter.And(
            Builders<FyersTokenDocument>.Filter.Eq(t => t.UserId, userId),
            Builders<FyersTokenDocument>.Filter.Eq(t => t.Status, FyersTokenStatus.Active));

        return await _tokens.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<FyersTokenDocument?> FindDirtyByUserIdAsync(
        string userId,
        CancellationToken ct = default)
    {
        var filter = Builders<FyersTokenDocument>.Filter.And(
            Builders<FyersTokenDocument>.Filter.Eq(t => t.UserId, userId),
            Builders<FyersTokenDocument>.Filter.Eq(t => t.Status, FyersTokenStatus.Dirty));

        return await _tokens.Find(filter).FirstOrDefaultAsync(ct);
    }

    // REQ-AUTH-009: marks the active token dirty.
    public async Task MarkDirtyAsync(
        string userId,
        CancellationToken ct = default)
    {
        var filter = Builders<FyersTokenDocument>.Filter.And(
            Builders<FyersTokenDocument>.Filter.Eq(t => t.UserId, userId),
            Builders<FyersTokenDocument>.Filter.Eq(t => t.Status, FyersTokenStatus.Active));

        var update = Builders<FyersTokenDocument>.Update
            .Set(t => t.Status, FyersTokenStatus.Dirty)
            .Set(t => t.UpdatedAt, DateTime.UtcNow);

        await _tokens.UpdateManyAsync(filter, update, cancellationToken: ct);
    }

    // REQ-AUTH-010: sync workflows check this before running.
    public async Task<bool> HasActiveTokenAsync(
        string userId,
        CancellationToken ct = default)
    {
        var filter = Builders<FyersTokenDocument>.Filter.And(
            Builders<FyersTokenDocument>.Filter.Eq(t => t.UserId, userId),
            Builders<FyersTokenDocument>.Filter.Eq(t => t.Status, FyersTokenStatus.Active),
            Builders<FyersTokenDocument>.Filter.Gt(t => t.ExpiresAt, DateTime.UtcNow));

        return await _tokens.Find(filter).AnyAsync(ct);
    }

    // REQ-MARKET-002b: returns the raw access token for browser-tier WebSocket use.
    // In local-dev mode the AccessTokenRef is the token value itself;
    // in production this resolves through Key Vault (caller's responsibility).
    public async Task<string?> GetAccessTokenAsync(
        string userId,
        CancellationToken ct = default)
    {
        var active = await FindActiveByUserIdAsync(userId, ct);
        return active?.AccessTokenRef;
    }
}
