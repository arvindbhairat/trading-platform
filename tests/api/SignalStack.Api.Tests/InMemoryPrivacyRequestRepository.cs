using System.Collections.Concurrent;
using SignalStack.Api.PrivacyRequest;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory <see cref="IPrivacyRequestRepository"/> for integration tests
/// that do not have a live MongoDB connection.
/// </summary>
public sealed class InMemoryPrivacyRequestRepository : IPrivacyRequestRepository
{
    private readonly ConcurrentDictionary<string, PrivacyRequestDocument> _byTicketId = new();
    private long _nextSeq;

    public Task<List<PrivacyRequestDocument>> ListAsync(CancellationToken ct = default)
    {
        var list = _byTicketId.Values
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        return Task.FromResult(list);
    }

    public Task<PrivacyRequestDocument?> FindByTicketIdAsync(
        string ticketId, CancellationToken ct = default)
    {
        _byTicketId.TryGetValue(ticketId, out var doc);
        return Task.FromResult(doc);
    }

    public Task<string> CreateAsync(PrivacyRequestDocument request, CancellationToken ct = default)
    {
        _byTicketId[request.TicketId] = request;
        return Task.FromResult(request.TicketId);
    }

    public Task<bool> AcknowledgeAsync(
        string ticketId, DateTime acknowledgedAt, CancellationToken ct = default)
    {
        if (!_byTicketId.TryGetValue(ticketId, out var doc))
            return Task.FromResult(false);

        if (doc.Status != RequestStatus.Open)
            return Task.FromResult(false);

        doc.Status = RequestStatus.Acknowledged;
        doc.AcknowledgedAt = acknowledgedAt;
        doc.UpdatedAt = acknowledgedAt;
        return Task.FromResult(true);
    }

    public Task<bool> UpdateStatusAsync(
        string ticketId, string newStatus, CancellationToken ct = default)
    {
        if (!_byTicketId.TryGetValue(ticketId, out var doc))
            return Task.FromResult(false);

        doc.Status = newStatus;
        doc.UpdatedAt = DateTime.UtcNow;
        return Task.FromResult(true);
    }

    public Task<bool> CompleteAsync(
        string ticketId, string resolutionNotes, DateTime completedAt,
        CancellationToken ct = default)
    {
        if (!_byTicketId.TryGetValue(ticketId, out var doc))
            return Task.FromResult(false);

        doc.Status = RequestStatus.Completed;
        doc.CompletedAt = completedAt;
        doc.ResolutionNotes = resolutionNotes;
        doc.UpdatedAt = completedAt;
        return Task.FromResult(true);
    }

    public Task<long> GetNextSequenceAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _nextSeq));
    }
}
