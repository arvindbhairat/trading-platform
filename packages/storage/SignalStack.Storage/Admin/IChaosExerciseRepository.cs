using MongoDB.Bson;
using SignalStack.Domain.Admin;

namespace SignalStack.Storage.Admin;

/// <summary>
/// Repository for the <c>chaos_exercises</c> MongoDB collection.
/// P8-T12 / REQ-NFR-014: chaos / failure-injection exercise recording.
/// </summary>
public interface IChaosExerciseRepository
{
    /// <summary>Returns all exercise executions, ordered by most recent first.</summary>
    Task<List<ChaosExerciseDocument>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Finds an exercise execution by its ObjectId. Returns null if not found.</summary>
    Task<ChaosExerciseDocument?> GetByIdAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Creates a new exercise execution record.</summary>
    Task CreateAsync(ChaosExerciseDocument document, CancellationToken ct = default);

    /// <summary>Updates mutable fields of an exercise execution record.</summary>
    Task UpdateAsync(ObjectId id, string? outcome = null, string? findings = null,
        string? remediation = null, CancellationToken ct = default);

    /// <summary>Returns true if at least one exercise execution exists with the given outcome for each exercise number in (1..5).</summary>
    Task<bool> AllFiveExercisesPassedOnceAsync(CancellationToken ct = default);
}
