using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Storage.SysConfig;

namespace SignalStack.Api.Risk;

/// <summary>
/// Channel registry capacity status for the idle-channel cap warning.
/// RME-L1: portal warning when usage reaches 80% of phase_a_max_active_channels.
/// </summary>
public sealed record RmeChannelStatusResult(
    int ActivePositionCount,
    int MaxActivePositions,
    decimal UsagePct,
    bool WarningActive,
    string? WarningMessage
);

/// <summary>
/// Reads active position count from MongoDB and the max channel cap from sys_config
/// to compute channel registry usage. Since the channel registry is in-process in the
/// Worker (cannot be queried from the API directly), counting non-terminal positions
/// from MongoDB is an accurate proxy under the single-instance invariant.
/// RME-L1 / REQ-RME-CONC-005.
/// </summary>
public sealed class RmeChannelStatusService
{
    private readonly IMongoDatabase _database;
    private readonly ISysConfigRepository _sysConfig;

    private const int DefaultMaxActivePositions = 200;

    public RmeChannelStatusService(
        IMongoDatabase database,
        ISysConfigRepository sysConfig)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _sysConfig = sysConfig ?? throw new ArgumentNullException(nameof(sysConfig));
    }

    public async Task<RmeChannelStatusResult> GetChannelStatusAsync(
        CancellationToken ct = default)
    {
        // ── 1. Count non-terminal positions across ALL users ─────────────────
        var positions = _database.GetCollection<BsonDocument>("positions");
        var activeCount = (int)await positions
            .Find(Builders<BsonDocument>.Filter.In("state",
                ["Open", "PendingEntry", "Suspended"]))
            .CountDocumentsAsync(ct);

        // ── 2. Read max active channels from sys_config ──────────────────────
        var maxActive = (int)await _sysConfig.GetLongAsync(
            "channel.lifecycle.phase_a_max_active_channels",
            DefaultMaxActivePositions,
            ct);

        if (maxActive <= 0) maxActive = DefaultMaxActivePositions;

        // ── 3. Compute usage ────────────────────────────────────────────────
        var usagePct = Math.Round((decimal)activeCount / maxActive * 100m, 1);

        // RME-L1: warning at 80% capacity
        var warningActive = usagePct >= 80m;
        string? warningMessage = null;

        if (warningActive)
        {
            warningMessage = usagePct >= 100m
                ? $"RME channel capacity reached ({activeCount}/{maxActive}). " +
                  "New position monitoring may be impacted. Contact admin."
                : $"RME channel usage is at {usagePct}% ({activeCount}/{maxActive}). " +
                  "Approaching capacity limit. Consider reviewing open positions.";
        }

        return new RmeChannelStatusResult(
            ActivePositionCount: activeCount,
            MaxActivePositions: maxActive,
            UsagePct: usagePct,
            WarningActive: warningActive,
            WarningMessage: warningMessage
        );
    }
}
