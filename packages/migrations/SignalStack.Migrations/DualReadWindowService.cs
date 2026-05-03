using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Migrations;

/// <summary>
/// Reads the <c>migrations.dual_read_window.active</c> sys_config flag to
/// determine whether dual-read mode is currently active.
///
/// REQ-MIGRATION-005: when a per-symbol DDL migration introduces a column
/// or changes a data shape that application code reads, the platform must
/// support a documented dual-read window during which both the old and new
/// schemas are accepted.  The flag is toggled by the admin; it is flipped
/// off only after the batched migration has been verified complete across
/// the full symbol master.
/// </summary>
public sealed class DualReadWindowService
{
    private const string ConfigKey = "migrations.dual_read_window.active";

    private readonly IMongoDatabase _database;
    private readonly ILogger<DualReadWindowService> _logger;

    // In-memory cache for the flag to avoid repeated DB reads within a single
    // request scope.  The cache is scoped to the service lifetime (typically
    // singleton — see remarks).
    //
    // In a singleton service the cached value may become stale within a single
    // process lifetime.  This is acceptable because:
    //   a) the flag is flipped by the admin ahead of a deployment, not during
    //      an active trading session; and
    //   b) a process restart picks up the latest value.
    // If real-time toggling is required in the future, replace with a Redis
    // pub/sub channel or periodic refresh.
    private bool? _cachedActive;
    private DateTime _cacheExpiry = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public DualReadWindowService(
        IMongoDatabase database,
        ILogger<DualReadWindowService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Returns <c>true</c> when the dual-read window is active, meaning
    /// application code must accept both the old and new schema shapes.
    /// </summary>
    public async Task<bool> IsDualReadActiveAsync(CancellationToken ct = default)
    {
        // ── Check in-memory cache ───────────────────────────────────────────
        if (_cachedActive.HasValue && DateTime.UtcNow < _cacheExpiry)
            return _cachedActive.Value;

        try
        {
            var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
            var filter = Builders<BsonDocument>.Filter.Eq("key", ConfigKey);
            var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

            if (doc is null)
            {
                _logger.LogWarning(
                    "sys_config key '{Key}' not found. Defaulting to dual-read inactive.",
                    ConfigKey);
                SetCache(false);
                return false;
            }

            var value = doc.GetValue("value", BsonValue.Create(false));
            var active = value.IsBoolean && value.AsBoolean;

            _logger.LogDebug("Dual-read window active: {Active}", active);
            SetCache(active);
            return active;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to read dual-read window flag from sys_config. " +
                "Defaulting to dual-read inactive.");
            SetCache(false);
            return false;
        }
    }

    /// <summary>
    /// Describes the two read paths that callers should attempt based on the
    /// current dual-read state.
    /// </summary>
    public async Task<DualReadMode> GetReadModeAsync(CancellationToken ct = default)
    {
        var dualRead = await IsDualReadActiveAsync(ct);
        return dualRead ? DualReadMode.BothPaths : DualReadMode.NewSchemaOnly;
    }

    private void SetCache(bool value)
    {
        _cachedActive = value;
        _cacheExpiry = DateTime.UtcNow.Add(CacheDuration);
    }
}

/// <summary>
/// Describes what read paths application code should attempt for a schema
/// that may be undergoing migration.
/// </summary>
public enum DualReadMode
{
    /// <summary>
    /// Read from the new schema only.  This is the steady-state mode after
    /// a batched migration has been verified complete.
    /// </summary>
    NewSchemaOnly,

    /// <summary>
    /// Attempt the new schema first; if the column or table is absent, fall
    /// back to the old schema.  Also known as "dual-read" or "read-forward"
    /// mode.
    /// </summary>
    BothPaths,
}
