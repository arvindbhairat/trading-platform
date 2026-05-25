using MongoDB.Bson;
using SignalStack.Domain.Admin;

namespace SignalStack.Storage.Admin;

/// <summary>
/// Repository for the <c>penetration_tests</c> MongoDB collection.
/// REQ-SEC-010: penetration test scheduling and remediation tracking.
/// </summary>
public interface IPenetrationTestRepository
{
    /// <summary>Returns all penetration test engagements, ordered by most recent first.</summary>
    Task<List<PenetrationTestDocument>> ListAllAsync(CancellationToken ct = default);

    /// <summary>Finds a penetration test engagement by its ObjectId. Returns null if not found.</summary>
    Task<PenetrationTestDocument?> GetByIdAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Creates a new penetration test engagement record.</summary>
    Task CreateAsync(PenetrationTestDocument document, CancellationToken ct = default);

    /// <summary>Updates the mutable fields of a penetration test engagement.</summary>
    Task UpdateAsync(
        ObjectId id,
        string? vendorName = null,
        List<string>? scope = null,
        DateTime? scheduledDate = null,
        DateTime? completedDate = null,
        string? status = null,
        string? engagementRef = null,
        CancellationToken ct = default);

    /// <summary>Adds a finding to a penetration test engagement.</summary>
    Task AddFindingAsync(ObjectId testId, PenetrationTestFinding finding, CancellationToken ct = default);

    /// <summary>Updates a finding's remediation status and notes.</summary>
    Task UpdateFindingAsync(
        ObjectId testId,
        string findingId,
        string? status = null,
        string? remediationNotes = null,
        CancellationToken ct = default);

    /// <summary>Returns unresolved high/critical findings count for a test engagement.</summary>
    Task<long> CountUnresolvedHighCriticalFindingsAsync(ObjectId testId, CancellationToken ct = default);
}
