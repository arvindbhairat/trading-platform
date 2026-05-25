using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Audit;

namespace SignalStack.Storage.Audit;

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

    public async Task<(IReadOnlyList<AuditEventDocument> Items, long TotalCount)> ListAsync(
        AuditEventFilter? filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var filterDef = BuildFilterDefinition(filter);
        var totalCount = await _auditEvents.CountDocumentsAsync(filterDef, cancellationToken: cancellationToken);

        var skip = (page - 1) * pageSize;
        var items = await _auditEvents
            .Find(filterDef)
            .SortByDescending(e => e.EventAt)
            .Skip(skip)
            .Limit(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public async Task<AuditEventDocument?> GetByIdAsync(
        ObjectId id,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<AuditEventDocument>.Filter.Eq(e => e.Id, id);
        return await _auditEvents.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    private static FilterDefinition<AuditEventDocument> BuildFilterDefinition(AuditEventFilter? filter)
    {
        if (filter is null)
            return Builders<AuditEventDocument>.Filter.Empty;

        var filters = new List<FilterDefinition<AuditEventDocument>>();

        if (filter.ActorId is not null)
            filters.Add(Builders<AuditEventDocument>.Filter.Eq(e => e.ActorId, filter.ActorId));

        if (filter.ActionType is not null)
            filters.Add(Builders<AuditEventDocument>.Filter.Eq(e => e.ActionType, filter.ActionType));

        if (filter.From.HasValue)
            filters.Add(Builders<AuditEventDocument>.Filter.Gte(e => e.EventAt, filter.From.Value));

        if (filter.To.HasValue)
            filters.Add(Builders<AuditEventDocument>.Filter.Lte(e => e.EventAt, filter.To.Value));

        return filters.Count switch
        {
            0 => Builders<AuditEventDocument>.Filter.Empty,
            1 => filters[0],
            _ => Builders<AuditEventDocument>.Filter.And(filters)
        };
    }
}
