using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Audit;

public sealed class MongoAuditEventRepository : IAuditEventRepository
{
    private readonly IMongoCollection<AuditEventDocument> _auditEvents;

    public MongoAuditEventRepository(IMongoDatabase database)
    {
        _auditEvents = database.GetCollection<AuditEventDocument>("audit_events");
    }

    public async Task<ObjectId> RecordAsync(
        string actorId,
        string actionType,
        DateTime eventAt,
        Dictionary<string, object?>? details = null,
        CancellationToken cancellationToken = default)
    {
        var doc = new AuditEventDocument
        {
            Id = ObjectId.GenerateNewId(),
            ActorId = actorId,
            ActionType = actionType,
            EventAt = eventAt,
            Details = details,
        };

        await _auditEvents.InsertOneAsync(doc, cancellationToken: cancellationToken);
        return doc.Id;
    }

    public async Task<ObjectId> RecordStepUpGatedAsync(
        string actorId,
        string actionType,
        DateTime eventAt,
        ObjectId stepUpEventId,
        Dictionary<string, object?>? details = null,
        CancellationToken cancellationToken = default)
    {
        var doc = new AuditEventDocument
        {
            Id = ObjectId.GenerateNewId(),
            ActorId = actorId,
            ActionType = actionType,
            EventAt = eventAt,
            Details = details,
            StepUpEventRef = stepUpEventId,
        };

        await _auditEvents.InsertOneAsync(doc, cancellationToken: cancellationToken);
        return doc.Id;
    }

    public async Task<bool> HasEventAsync(
        string actorId,
        string actionType,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<AuditEventDocument>.Filter.And(
            Builders<AuditEventDocument>.Filter.Eq(e => e.ActorId, actorId),
            Builders<AuditEventDocument>.Filter.Eq(e => e.ActionType, actionType));

        return await _auditEvents.Find(filter).AnyAsync(cancellationToken);
    }
}
