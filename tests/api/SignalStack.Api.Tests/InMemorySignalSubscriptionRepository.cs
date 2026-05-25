using System.Collections.Concurrent;
using MongoDB.Bson;
using SignalStack.Api.Signals;
using SignalStack.Domain.Signals;
using SignalStack.Storage.Signals;

namespace SignalStack.Api.Tests;

/// <summary>
/// Thread-safe in-memory <see cref="ISignalSubscriptionRepository"/> for integration tests.
/// Implements the full contract: CRUD, pause/resume, versioned COW, active-by-user-at snapshot.
/// </summary>
public sealed class InMemorySignalSubscriptionRepository : ISignalSubscriptionRepository
{
    private readonly ConcurrentDictionary<ObjectId, SignalSubscriptionDocument> _store = new();

    public Task<List<SignalSubscriptionDocument>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        var result = _store.Values
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<SignalSubscriptionDocument?> GetByIdAsync(
        ObjectId id, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var doc);
        return Task.FromResult<SignalSubscriptionDocument?>(doc);
    }

    public Task<SignalSubscriptionDocument?> GetByIdAndUserAsync(
        ObjectId id, string userId, CancellationToken ct = default)
    {
        _store.TryGetValue(id, out var doc);
        return Task.FromResult(doc?.UserId == userId ? doc : null);
    }

    public Task CreateAsync(SignalSubscriptionDocument subscription, CancellationToken ct = default)
    {
        if (!_store.TryAdd(subscription.Id, subscription))
            throw new InvalidOperationException($"Duplicate subscription ID: {subscription.Id}");
        return Task.CompletedTask;
    }

    public Task ReplaceAsync(SignalSubscriptionDocument subscription, CancellationToken ct = default)
    {
        _store[subscription.Id] = subscription;
        return Task.CompletedTask;
    }

    public Task UpdateStatusAsync(
        ObjectId id, string newStatus, DateTime updatedAt, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var doc))
        {
            doc.Status = newStatus;
            doc.UpdatedAt = updatedAt;
        }
        return Task.CompletedTask;
    }

    public Task AddVersionAsync(
        ObjectId id, RmeConfigurationVersion newVersion, int newPendingIndex,
        DateTime updatedAt, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var doc))
        {
            doc.Versions.Add(newVersion);
            doc.PendingVersionIndex = newPendingIndex;
            doc.UpdatedAt = updatedAt;
        }
        return Task.CompletedTask;
    }

    public Task DiscardPendingVersionAsync(
        ObjectId id, DateTime updatedAt, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var doc))
        {
            doc.PendingVersionIndex = null;
            doc.UpdatedAt = updatedAt;
        }
        return Task.CompletedTask;
    }

    public Task ActivatePendingVersionAsync(
        ObjectId id, int newCurrentIndex, DateTime updatedAt, CancellationToken ct = default)
    {
        if (_store.TryGetValue(id, out var doc))
        {
            doc.CurrentVersionIndex = newCurrentIndex;
            doc.PendingVersionIndex = null;
            doc.UpdatedAt = updatedAt;
        }
        return Task.CompletedTask;
    }

    public Task<long> CountActiveByUserAsync(string userId, CancellationToken ct = default)
    {
        var count = _store.Values.LongCount(s =>
            s.UserId == userId && s.Status == SubscriptionStatus.Active);
        return Task.FromResult(count);
    }

    public Task<List<SignalSubscriptionDocument>> GetAllActiveAsync(CancellationToken ct = default)
    {
        var result = _store.Values
            .Where(s => s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<List<SignalSubscriptionDocument>> GetActiveByUserAtAsync(
        string userId, DateTime at, CancellationToken ct = default)
    {
        var result = _store.Values
            .Where(s => s.UserId == userId
                        && s.Status == SubscriptionStatus.Active
                        && s.CreatedAt <= at)
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
        return Task.FromResult(result);
    }

    public Task DeleteAsync(ObjectId id, CancellationToken ct = default)
    {
        _store.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
