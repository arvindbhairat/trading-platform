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
}
