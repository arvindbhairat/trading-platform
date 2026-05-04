using System.Collections.Concurrent;
using MongoDB.Bson;
using SignalStack.Api.Audit;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory <see cref="IAuditEventRepository"/> for integration tests that do not
/// have a live MongoDB connection.
/// </summary>
public sealed class InMemoryAuditEventRepository : IAuditEventRepository
{
    private readonly ConcurrentBag<AuditEventDocument> _events = new();

    /// <summary>Returns a snapshot of all recorded events for test assertions.</summary>
    public IReadOnlyList<AuditEventDocument> GetEvents() => _events.ToList().AsReadOnly();

    /// <summary>Clears all recorded events. Used between tests to avoid shared-state contamination.</summary>
    public void Clear()
    {
        while (_events.TryTake(out _)) { }
    }

    public Task<ObjectId> RecordAsync(
        string actorId, string actionType, DateTime eventAt,
        Dictionary<string, object?>? details = null,
        CancellationToken cancellationToken = default)
    {
        var id = ObjectId.GenerateNewId();
        _events.Add(new AuditEventDocument
        {
            Id = id,
            ActorId = actorId,
            ActionType = actionType,
            EventAt = eventAt,
            Details = details,
        });
        return Task.FromResult(id);
    }

    public Task<ObjectId> RecordStepUpGatedAsync(
        string actorId, string actionType, DateTime eventAt,
        ObjectId stepUpEventId,
        Dictionary<string, object?>? details = null,
        CancellationToken cancellationToken = default)
    {
        var id = ObjectId.GenerateNewId();
        _events.Add(new AuditEventDocument
        {
            Id = id,
            ActorId = actorId,
            ActionType = actionType,
            EventAt = eventAt,
            Details = details,
            StepUpEventRef = stepUpEventId,
        });
        return Task.FromResult(id);
    }

    public Task<bool> HasEventAsync(
        string actorId, string actionType,
        CancellationToken cancellationToken = default)
    {
        var found = _events.Any(e =>
            e.ActorId == actorId && e.ActionType == actionType);
        return Task.FromResult(found);
    }

    public Task<(IReadOnlyList<AuditEventDocument> Items, long TotalCount)> ListAsync(
        AuditEventFilter? filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _events.AsEnumerable();

        if (filter?.ActorId is not null)
            query = query.Where(e => e.ActorId == filter.ActorId);
        if (filter?.ActionType is not null)
            query = query.Where(e => e.ActionType == filter.ActionType);
        if (filter?.From is not null)
            query = query.Where(e => e.EventAt >= filter.From.Value);
        if (filter?.To is not null)
            query = query.Where(e => e.EventAt <= filter.To.Value);

        var ordered = query.OrderByDescending(e => e.EventAt).ToList();
        var totalCount = ordered.Count;

        var skip = (page - 1) * pageSize;
        var items = ordered.Skip(skip).Take(pageSize).ToList();

        return Task.FromResult<(IReadOnlyList<AuditEventDocument>, long)>(
            (items.AsReadOnly(), totalCount));
    }

    public Task<AuditEventDocument?> GetByIdAsync(
        ObjectId id,
        CancellationToken cancellationToken = default)
    {
        var found = _events.FirstOrDefault(e => e.Id == id);
        return Task.FromResult(found);
    }
}
