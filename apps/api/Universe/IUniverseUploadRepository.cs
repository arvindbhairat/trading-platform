using MongoDB.Bson;

namespace SignalStack.Api.Universe;

/// <summary>
/// Repository for the <c>universe_upload_results</c> MongoDB collection.
/// Covers REQ-UNIV-011..020.
/// </summary>
public interface IUniverseUploadRepository
{
    /// <summary>Inserts an upload result record.</summary>
    Task CreateAsync(UniverseUploadResultDocument document, CancellationToken ct = default);

    /// <summary>Returns the most recent upload, or null if none.</summary>
    Task<UniverseUploadResultDocument?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>Returns all uploads, newest first.</summary>
    Task<List<UniverseUploadResultDocument>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Finds an upload by ID.</summary>
    Task<UniverseUploadResultDocument?> FindByIdAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Marks an upload as rolled back.</summary>
    Task MarkRolledBackAsync(ObjectId id, string rolledBackBy, DateTime rolledBackAt, CancellationToken ct = default);

    /// <summary>
    /// Returns the pre-upload snapshot for a given upload ID.
    /// Null if the snapshot retention period has expired.
    /// </summary>
    Task<UniverseSnapshot?> GetSnapshotAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Deletes snapshots older than the given cutoff date.</summary>
    Task PruneSnapshotsAsync(DateTime cutoff, CancellationToken ct = default);
}
