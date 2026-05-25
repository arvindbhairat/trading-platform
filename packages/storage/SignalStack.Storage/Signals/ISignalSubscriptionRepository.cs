using MongoDB.Bson;
using SignalStack.Domain.Signals;

namespace SignalStack.Storage.Signals;

/// <summary>
/// Repository for the <c>signals</c> MongoDB collection.
/// Covers REQ-STRAT-007a/007b (pause/resume) and REQ-STRAT-017b (versioned COW).
/// </summary>
public interface ISignalSubscriptionRepository
{
    /// <summary>Returns all subscriptions for a user, ordered by created_at desc.</summary>
    Task<List<SignalSubscriptionDocument>> GetByUserIdAsync(
        string userId, CancellationToken ct = default);

    /// <summary>Returns a single subscription by its ID. Null if not found.</summary>
    Task<SignalSubscriptionDocument?> GetByIdAsync(
        ObjectId id, CancellationToken ct = default);

    /// <summary>Returns a single subscription by ID, verifying ownership.</summary>
    Task<SignalSubscriptionDocument?> GetByIdAndUserAsync(
        ObjectId id, string userId, CancellationToken ct = default);

    /// <summary>Creates a new Signal Subscription.</summary>
    Task CreateAsync(SignalSubscriptionDocument subscription, CancellationToken ct = default);

    /// <summary>
    /// Replaces the subscription document (for full updates).
    /// Uses optimistic concurrency via the document's version field.
    /// </summary>
    Task ReplaceAsync(SignalSubscriptionDocument subscription, CancellationToken ct = default);

    /// <summary>
    /// Updates the status field only (active/paused).
    /// REQ-STRAT-007a: pause/resume.
    /// </summary>
    Task UpdateStatusAsync(
        ObjectId id, string newStatus, DateTime updatedAt, CancellationToken ct = default);

    /// <summary>
    /// Appends a new RME configuration version and updates the pending version index.
    /// REQ-STRAT-017b: copy-on-write version creation.
    /// </summary>
    Task AddVersionAsync(
        ObjectId id,
        RmeConfigurationVersion newVersion,
        int newPendingIndex,
        DateTime updatedAt,
        CancellationToken ct = default);

    /// <summary>
    /// Discards the pending version, reverting the pending_version_index to null.
    /// REQ-STRAT-017b: discard pending before it takes effect.
    /// </summary>
    Task DiscardPendingVersionAsync(
        ObjectId id, DateTime updatedAt, CancellationToken ct = default);

    /// <summary>
    /// Activates a pending version by updating current_version_index and clearing
    /// pending_version_index. Called at EODSR start.
    /// </summary>
    Task ActivatePendingVersionAsync(
        ObjectId id, int newCurrentIndex, DateTime updatedAt, CancellationToken ct = default);

    /// <summary>Returns the count of active (non-paused) subscriptions for a user.</summary>
    Task<long> CountActiveByUserAsync(string userId, CancellationToken ct = default);

    /// <summary>
    /// Returns all active subscriptions for all users.
    /// Used by EODSR to read subscription state once at job start. REQ-STRAT-007b.
    /// </summary>
    Task<List<SignalSubscriptionDocument>> GetAllActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns subscriptions for a specific user that were active at a snapshot time.
    /// Used by EODSR atomic-run snapshot (reads state once at job start).
    /// </summary>
    Task<List<SignalSubscriptionDocument>> GetActiveByUserAtAsync(
        string userId, DateTime at, CancellationToken ct = default);

    /// <summary>Permanently deletes a subscription document.</summary>
    Task DeleteAsync(ObjectId id, CancellationToken ct = default);
}
