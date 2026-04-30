using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Sessions;

/// <summary>
/// MongoDB-backed session repository.  Uses a client-session transaction to
/// atomically delete prior sessions and insert the new one (REQ-SESSION-002a).
/// </summary>
public sealed class MongoSessionRepository : ISessionRepository
{
    private readonly IMongoCollection<SessionDocument> _sessions;
    private readonly IMongoClient _client;

    public MongoSessionRepository(IMongoDatabase database, IMongoClient client)
    {
        _sessions = database.GetCollection<SessionDocument>("sessions");
        _client = client;
    }

    // REQ-SESSION-002 / REQ-SESSION-002a: delete all prior sessions for the user and
    // insert the new one in a single MongoDB transaction (single atomic operation).
    public async Task CreateSessionAsync(
        string userId,
        string sessionToken,
        DateTime issuedAt,
        DateTime expiresAt,
        string? userAgent,
        DateTime? mfaVerifiedAt = null,
        CancellationToken cancellationToken = default)
    {
        var doc = new SessionDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            SessionToken = sessionToken,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            UserAgent = userAgent,
            MfaVerifiedAt = mfaVerifiedAt,
        };

        using var txnSession = await _client.StartSessionAsync(cancellationToken: cancellationToken);
        txnSession.StartTransaction();
        try
        {
            await _sessions.DeleteManyAsync(
                txnSession,
                Builders<SessionDocument>.Filter.Eq(s => s.UserId, userId),
                cancellationToken: cancellationToken);

            await _sessions.InsertOneAsync(txnSession, doc, cancellationToken: cancellationToken);
            await txnSession.CommitTransactionAsync(cancellationToken);
        }
        catch
        {
            await txnSession.AbortTransactionAsync(cancellationToken);
            throw;
        }
    }

    public async Task<SessionDocument?> FindBySessionTokenAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        return await _sessions
            .Find(Builders<SessionDocument>.Filter.Eq(s => s.SessionToken, sessionToken))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<SessionDocument?> FindByUserIdAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        return await _sessions
            .Find(Builders<SessionDocument>.Filter.Eq(s => s.UserId, userId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    // REQ-SEC-011: records the step-up timestamp on the current session.
    public async Task UpdateStepUpAsync(
        string sessionToken,
        DateTime stepUpAuthenticatedAt,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<SessionDocument>.Filter.Eq(s => s.SessionToken, sessionToken);
        var update = Builders<SessionDocument>.Update
            .Set(s => s.StepUpAuthenticatedAt, stepUpAuthenticatedAt);

        await _sessions.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }
}
