using MongoDB.Bson;

namespace SignalStack.Api.SysConfig;

public interface ISysConfigRepository
{
    // Returns operations.phase_a.tester_ceiling; defaults to 30 when not yet seeded.
    Task<long> GetTesterCeilingAsync(CancellationToken ct = default);

    // Returns operations.phase.current; defaults to "A" when not yet seeded.
    Task<string> GetCurrentPhaseAsync(CancellationToken ct = default);

    // Returns platform.seed.version; null when not yet seeded (REQ-CONFIG-010).
    // Used by the sentinel startup check — API/Worker fail fast when absent.
    Task<string?> GetSeedVersionAsync(CancellationToken ct = default);

    // === Admin management methods (P2-T11 / REQ-CONFIG-005) ===

    /// <summary>Returns all sys_config entries, optionally filtered by category.</summary>
    Task<List<BsonDocument>> ListAllAsync(string? category = null, CancellationToken ct = default);

    /// <summary>Returns a single sys_config entry by key, or null if not found.</summary>
    Task<BsonDocument?> GetByKeyAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Updates the value, updatedAt, updatedByUserId, and increments version.
    /// Returns the updated document, or null if the key was not found.
    /// </summary>
    Task<BsonDocument?> UpdateAsync(string key, BsonValue newValue, string updatedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Resets value to defaultValue, updates updatedAt/updatedByUserId, increments version.
    /// Returns the updated document, or null if the key was not found.
    /// </summary>
    Task<BsonDocument?> ResetToDefaultAsync(string key, string updatedByUserId, CancellationToken ct = default);

    // Returns the list of distinct categories present in sys_config.
    Task<List<string>> ListCategoriesAsync(CancellationToken ct = default);
}
