using MongoDB.Bson;
using SignalStack.Domain.Portfolio;

namespace SignalStack.Storage.Portfolio;

/// <summary>
/// Repository contract for the <c>manual_adjustments</c> collection.
///
/// REQ-RECON-003/004: adjustment entries recorded with full audit trail.
/// </summary>
public interface IManualAdjustmentRepository
{
    /// <summary>Inserts a manual adjustment record. Returns the generated ObjectId.</summary>
    Task<ObjectId> InsertAsync(ManualAdjustmentDocument adjustment, CancellationToken ct = default);

    /// <summary>Retrieves all adjustments for a user, most recent first.</summary>
    Task<List<ManualAdjustmentDocument>> GetByUserAsync(string userId, int limit = 50, CancellationToken ct = default);

    /// <summary>Retrieves adjustments for a specific symbol and user, most recent first.</summary>
    Task<List<ManualAdjustmentDocument>> GetByUserAndSymbolAsync(string userId, string symbol, int limit = 50, CancellationToken ct = default);

    /// <summary>Retrieves a single adjustment by its ID.</summary>
    Task<ManualAdjustmentDocument?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
}
