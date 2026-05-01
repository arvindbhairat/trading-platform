using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.PrivacyRequest;

public sealed class MongoPrivacyRequestRepository : IPrivacyRequestRepository
{
    private readonly IMongoCollection<PrivacyRequestDocument> _requests;
    private readonly IMongoCollection<BsonDocument> _counters;

    public MongoPrivacyRequestRepository(IMongoDatabase database)
    {
        _requests = database.GetCollection<PrivacyRequestDocument>("privacy_requests");
        _counters = database.GetCollection<BsonDocument>("counters");
    }

    public async Task<List<PrivacyRequestDocument>> ListAsync(CancellationToken ct = default)
    {
        return await _requests
            .Find(Builders<PrivacyRequestDocument>.Filter.Empty)
            .Sort(Builders<PrivacyRequestDocument>.Sort.Descending(r => r.CreatedAt))
            .ToListAsync(ct);
    }

    public async Task<PrivacyRequestDocument?> FindByTicketIdAsync(
        string ticketId, CancellationToken ct = default)
    {
        return await _requests
            .Find(Builders<PrivacyRequestDocument>.Filter.Eq(r => r.TicketId, ticketId))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<string> CreateAsync(
        PrivacyRequestDocument request, CancellationToken ct = default)
    {
        await _requests.InsertOneAsync(request, cancellationToken: ct);
        return request.TicketId;
    }

    public async Task<bool> AcknowledgeAsync(
        string ticketId, DateTime acknowledgedAt, CancellationToken ct = default)
    {
        var filter = Builders<PrivacyRequestDocument>.Filter.And(
            Builders<PrivacyRequestDocument>.Filter.Eq(r => r.TicketId, ticketId),
            Builders<PrivacyRequestDocument>.Filter.Eq(r => r.Status, RequestStatus.Open));

        var update = Builders<PrivacyRequestDocument>.Update
            .Set(r => r.Status, RequestStatus.Acknowledged)
            .Set(r => r.AcknowledgedAt, acknowledgedAt)
            .Set(r => r.UpdatedAt, acknowledgedAt);

        var result = await _requests.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateStatusAsync(
        string ticketId, string newStatus, CancellationToken ct = default)
    {
        var filter = Builders<PrivacyRequestDocument>.Filter.Eq(r => r.TicketId, ticketId);
        var update = Builders<PrivacyRequestDocument>.Update
            .Set(r => r.Status, newStatus)
            .Set(r => r.UpdatedAt, DateTime.UtcNow);

        var result = await _requests.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> CompleteAsync(
        string ticketId, string resolutionNotes, DateTime completedAt,
        CancellationToken ct = default)
    {
        var filter = Builders<PrivacyRequestDocument>.Filter.Eq(r => r.TicketId, ticketId);
        var update = Builders<PrivacyRequestDocument>.Update
            .Set(r => r.Status, RequestStatus.Completed)
            .Set(r => r.CompletedAt, completedAt)
            .Set(r => r.ResolutionNotes, resolutionNotes)
            .Set(r => r.UpdatedAt, completedAt);

        var result = await _requests.UpdateOneAsync(filter, update, cancellationToken: ct);
        return result.ModifiedCount > 0;
    }

    public async Task<long> GetNextSequenceAsync(CancellationToken ct = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", "privacy_request_ticket");
        var update = new BsonDocument("$inc", new BsonDocument("seq", 1L));
        var options = new FindOneAndUpdateOptions<BsonDocument>
        {
            ReturnDocument = ReturnDocument.After,
            IsUpsert = true,
        };

        var result = await _counters.FindOneAndUpdateAsync(filter, update, options, ct);
        return result["seq"].AsInt64;
    }
}
