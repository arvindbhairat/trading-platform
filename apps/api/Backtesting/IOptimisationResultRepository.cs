using MongoDB.Bson;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// Repository for storing and retrieving RME profile optimisation results.
/// REQ-RME-021: results stored per symbol; staleness evaluated at read time.
/// </summary>
public interface IOptimisationResultRepository
{
    /// <summary>Saves a new optimisation run document.</summary>
    Task SaveAsync(OptimisationResultDocument document, CancellationToken ct = default);

    /// <summary>Updates the completion status and results for a run.</summary>
    Task UpdateCompletionAsync(
        ObjectId runId,
        List<ProfileConfigResult> results,
        DateTime completedAt,
        CancellationToken ct = default);

    /// <summary>Marks a run as failed.</summary>
    Task MarkFailedAsync(ObjectId runId, CancellationToken ct = default);

    /// <summary>
    /// Returns the latest completed optimisation result for a subscription + symbol,
    /// or null if none exists.
    /// </summary>
    Task<OptimisationResultDocument?> GetLatestAsync(
        ObjectId subscriptionId, string symbol, CancellationToken ct = default);

    /// <summary>
    /// Returns an active (running) optimisation run for a subscription + symbol,
    /// or null if none exists. Used to prevent duplicate triggers (REQ-RME-020).
    /// </summary>
    Task<OptimisationResultDocument?> GetActiveRunAsync(
        ObjectId subscriptionId, string symbol, CancellationToken ct = default);

    /// <summary>
    /// Returns a specific run by ID.
    /// </summary>
    Task<OptimisationResultDocument?> GetByIdAsync(ObjectId runId, CancellationToken ct = default);

    /// <summary>
    /// Lists optimisation runs for a user, most recent first.
    /// </summary>
    Task<List<OptimisationResultDocument>> ListByUserAsync(
        string userId, int limit = 20, int offset = 0, CancellationToken ct = default);
}
