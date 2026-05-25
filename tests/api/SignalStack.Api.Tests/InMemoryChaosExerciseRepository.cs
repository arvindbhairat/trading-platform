using System.Collections.Concurrent;
using MongoDB.Bson;
using SignalStack.Api.Admin;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory <see cref="IChaosExerciseRepository"/> for integration tests.
/// </summary>
public sealed class InMemoryChaosExerciseRepository : IChaosExerciseRepository
{
    private readonly ConcurrentBag<ChaosExerciseDocument> _exercises = new();

    public IReadOnlyList<ChaosExerciseDocument> GetExercises() => _exercises.ToList().AsReadOnly();

    public void Clear()
    {
        while (_exercises.TryTake(out _)) { }
    }

    public Task<List<ChaosExerciseDocument>> ListAllAsync(CancellationToken ct = default)
    {
        var sorted = _exercises
            .OrderByDescending(e => e.ExecutedAt)
            .ToList();
        return Task.FromResult(sorted);
    }

    public Task<ChaosExerciseDocument?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
    {
        var found = _exercises.FirstOrDefault(e => e.Id == id);
        return Task.FromResult(found);
    }

    public Task CreateAsync(ChaosExerciseDocument document, CancellationToken ct = default)
    {
        _exercises.Add(document);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ObjectId id, string? outcome = null, string? findings = null,
        string? remediation = null, CancellationToken ct = default)
    {
        var existing = _exercises.FirstOrDefault(e => e.Id == id);
        if (existing is not null)
        {
            var list = _exercises.ToList();
            var idx = list.FindIndex(e => e.Id == id);
            if (idx >= 0)
            {
                if (outcome is not null) list[idx].Outcome = outcome;
                if (findings is not null) list[idx].Findings = findings;
                if (remediation is not null) list[idx].Remediation = remediation;
                list[idx].UpdatedAt = DateTime.UtcNow;
            }
            while (_exercises.TryTake(out _)) { }
            foreach (var item in list)
                _exercises.Add(item);
        }
        return Task.CompletedTask;
    }

    public Task<bool> AllFiveExercisesPassedOnceAsync(CancellationToken ct = default)
    {
        var passed = _exercises
            .Where(e => e.Outcome is "pass" or "walkthrough")
            .Select(e => e.ExerciseNumber)
            .Distinct()
            .Count();
        return Task.FromResult(passed >= 5);
    }
}
