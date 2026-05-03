namespace SignalStack.Worker.Rme;

/// <summary>
/// Repository for position documents in MongoDB.
///
/// Every write uses Optimistic Concurrency Control via the <c>_version</c> field
/// (REQ-RME-CONC-002) and optionally the fencing-token filter for stale-write
/// detection (REQ-PORT-031b(b)).
/// </summary>
public interface IPositionRepository
{
    /// <summary>Read a position by its identifier. Returns null when not found.</summary>
    Task<PositionDocument?> GetByIdAsync(Guid positionId, CancellationToken ct = default);

    /// <summary>Insert a new position document.</summary>
    Task CreateAsync(PositionDocument position, CancellationToken ct = default);

    /// <summary>
    /// Optimistic concurrency write using the <c>_version</c> filter (REQ-RME-CONC-002).
    ///
    /// Filter: <c>{ _id: positionId, _version: expectedVersion }</c>
    /// Optionally extended with the fencing-token guard when <paramref name="fencingToken"/>
    /// is non-null: <c>{ ledger_fence_token: { $lte: fencingToken } }</c>.
    ///
    /// On success: <paramref name="position"/>.Version is incremented and true is returned.
    /// On OCC conflict: <paramref name="position"/>.Version is unchanged and false is returned.
    /// </summary>
    Task<PositionOccResult> UpdateWithOccAsync(
        PositionDocument position,
        long expectedVersion,
        long? fencingToken = null,
        CancellationToken ct = default);

    /// <summary>
    /// Load all non-terminal (PendingEntry, Open, Suspended) position documents.
    /// Called at worker startup to rebuild the channel registry (REQ-RME-CONC-005c).
    /// </summary>
    Task<List<PositionDocument>> GetNonTerminalAsync(CancellationToken ct = default);
}

/// <summary>
/// Result of an OCC-protected position write.
/// </summary>
public readonly struct PositionOccResult
{
    public PositionOccResult(bool success, long newVersion)
    {
        Success = success;
        NewVersion = newVersion;
    }

    /// <summary>True when the write was accepted (MatchedCount > 0).</summary>
    public bool Success { get; }

    /// <summary>
    /// The new version after a successful write. Equal to expectedVersion + 1.
    /// Undefined (0) when Success is false.
    /// </summary>
    public long NewVersion { get; }
}
