using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Storage.SysConfig;

namespace SignalStack.Api.Risk;

/// <summary>
/// Portfolio health data for the persistent health strip on the chart page.
/// P6-T27 — combines equity, heat, position count, and drawdown state.
/// </summary>
public sealed record PortfolioHealthResult(
    decimal AccountEquity,
    decimal PortfolioHeatPct,
    decimal MaxHeatPct,
    decimal DrawdownPct,
    decimal HighWaterMark,
    int OpenPositionCount,
    bool EntryBlocked,
    string? EntryBlockedReason,
    string? DrawdownAdvisory,
    string HealthStatus,  // "good" | "caution" | "warning" | "critical"
    DateTime? DataFreshnessTimestamp,
    string? DrawdownModeLevel
);

/// <summary>
/// Computes portfolio health data for the health strip on the chart page.
/// P6-T27: reads equity, drawdown, positions, and heat from MongoDB and sys_config.
/// </summary>
public sealed class PortfolioHealthService
{
    private readonly IMongoDatabase _database;
    private readonly ISysConfigRepository _sysConfig;

    public PortfolioHealthService(
        IMongoDatabase database,
        ISysConfigRepository sysConfig)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _sysConfig = sysConfig ?? throw new ArgumentNullException(nameof(sysConfig));
    }

    public async Task<PortfolioHealthResult> ComputeHealthAsync(
        string userId,
        CancellationToken ct = default)
    {
        // ── 1. Read equity ──────────────────────────────────────────────────
        var equity = await ReadEquityAsync(userId, ct);
        var hwm = await ReadHighWaterMarkAsync(userId, equity, ct);

        // ── 2. Drawdown ─────────────────────────────────────────────────────
        var drawdownPct = hwm > 0 && equity < hwm
            ? Math.Round((hwm - equity) / hwm * 100m, 2)
            : 0m;

        // ── 3. Positions ────────────────────────────────────────────────────
        var positions = _database.GetCollection<BsonDocument>("positions");
        var openPositions = await positions
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                & Builders<BsonDocument>.Filter.In("state", ["Open", "PendingEntry"]))
            .ToListAsync(ct);

        var suspendedPositions = await positions
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                & Builders<BsonDocument>.Filter.Eq("state", "Suspended"))
            .ToListAsync(ct);

        var allActive = openPositions.Concat(suspendedPositions).ToList();

        // ── 4. Portfolio heat ───────────────────────────────────────────────
        var totalOpenRisk = allActive
            .Sum(p => CalcRisk(p));

        var maxHeatPct = await _sysConfig.GetDecimalAsync(
            "risk.default.max_portfolio_heat_pct", 5m, ct);

        var portfolioHeatPct = equity > 0
            ? Math.Round(totalOpenRisk / equity * 100m, 2)
            : 0m;

        // ── 5. Entry blocked? ───────────────────────────────────────────────
        var entryBlocked = portfolioHeatPct >= maxHeatPct;
        var entryBlockedReason = entryBlocked
            ? $"Portfolio heat ({portfolioHeatPct}%) at or above maximum ({maxHeatPct}%)."
            : null;

        // ── 6. Drawdown advisory ────────────────────────────────────────────
        string? drawdownAdvisory = drawdownPct switch
        {
            >= 30m => "Drawdown at 30%+ — advisory to stop all trading.",
            >= 20m => "Drawdown at 20%+ — new entries blocked. Consider reducing positions.",
            >= 15m => "Drawdown at 15%+ — advisory to reduce open positions.",
            >= 10m => "Drawdown at 10%+ — add-on entries suppressed.",
            >= 5m => $"Drawdown at {drawdownPct}% — position sizing reduced proportionally.",
            _ => null
        };

        // ── 7. Portfolio-level freshness indicator (T-9) ────────────────────
        // Timestamp = min(LADS completion, LMDS price update, equity-base sync).
        var ladsCompletedAt = await ReadLadsCompletionAsync(userId, ct);
        var lmdsUpdatedAt = await ReadLmdsPriceUpdateAsync(userId, ct);
        var equityBaseSyncAt = await ReadEquityBaseSyncAsync(userId, ct);

        var candidates = new[] { ladsCompletedAt, lmdsUpdatedAt, equityBaseSyncAt }
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToArray();

        DateTime? dataFreshnessTimestamp = candidates.Length > 0
            ? candidates.Min()
            : null;

        // ── 8. Drawdown mode level (REQ-DASH-012) ───────────────────────────
        string? drawdownModeLevel = drawdownPct switch
        {
            >= 30m => "Level 6 — Advisory to stop all trading",
            >= 25m => "Level 5 — Advisory to close weakest positions",
            >= 20m => "Level 4 — New entries blocked",
            >= 15m => "Level 3 — Advisory to reduce positions",
            >= 10m => "Level 2 — No add-on entries",
            >= 5m  => "Level 1 — Size reduction",
            _      => null
        };

        // ── 9. Overall health ───────────────────────────────────────────────
        var healthStatus = equity <= 0
            ? "caution"
            : drawdownPct >= 20m || entryBlocked
                ? "critical"
                : drawdownPct >= 10m || portfolioHeatPct >= maxHeatPct * 0.8m
                    ? "warning"
                    : drawdownPct >= 5m
                        ? "caution"
                        : "good";

        return new PortfolioHealthResult(
            AccountEquity: equity,
            PortfolioHeatPct: portfolioHeatPct,
            MaxHeatPct: maxHeatPct,
            DrawdownPct: drawdownPct,
            HighWaterMark: hwm,
            OpenPositionCount: openPositions.Count,
            EntryBlocked: entryBlocked,
            EntryBlockedReason: entryBlockedReason,
            DrawdownAdvisory: drawdownAdvisory,
            HealthStatus: healthStatus,
            DataFreshnessTimestamp: dataFreshnessTimestamp,
            DrawdownModeLevel: drawdownModeLevel
        );
    }

    private async Task<decimal> ReadEquityAsync(string userId, CancellationToken ct)
    {
        // Read from equity_curve first, fallback to portfolio_snapshots
        var equityCurve = _database.GetCollection<BsonDocument>("equity_curve");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var sort = Builders<BsonDocument>.Sort.Descending("session_date");

        var latest = await equityCurve
            .Find(filter)
            .Sort(sort)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (latest is not null)
        {
            var val = latest.GetValue("equity_value", BsonNull.Value);
            if (!val.IsBsonNull && val.IsDecimal128)
                return (decimal)val.AsDecimal128;
        }

        // Fallback to portfolio_snapshots
        var snapshots = _database.GetCollection<BsonDocument>("portfolio_snapshots");
        var snap = await snapshots.Find(
            Builders<BsonDocument>.Filter.Eq("user_id", userId))
            .FirstOrDefaultAsync(ct);

        if (snap is not null)
        {
            var mv = snap.GetValue("current_market_value", BsonNull.Value);
            var cash = snap.GetValue("cash_reserve", BsonNull.Value);
            var mvVal = !mv.IsBsonNull && mv.IsDecimal128 ? (decimal)mv.AsDecimal128 : 0m;
            var cashVal = !cash.IsBsonNull && cash.IsDecimal128 ? (decimal)cash.AsDecimal128 : 0m;
            return mvVal + cashVal;
        }

        return 0m;
    }

    private async Task<decimal> ReadHighWaterMarkAsync(
        string userId, decimal currentEquity, CancellationToken ct)
    {
        var equityCurve = _database.GetCollection<BsonDocument>("equity_curve");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var sort = Builders<BsonDocument>.Sort.Descending("equity_value");

        var highest = await equityCurve
            .Find(filter)
            .Sort(sort)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (highest is not null)
        {
            var val = highest.GetValue("equity_value", BsonNull.Value);
            if (!val.IsBsonNull && val.IsDecimal128)
                return Math.Max((decimal)val.AsDecimal128, currentEquity);
        }

        return currentEquity;
    }

    private static decimal CalcRisk(BsonDocument p)
    {
        var entry = GetDecimal(p, "entry_price");
        var stop = GetDecimal(p, "stop_loss");
        var qty = GetDecimal(p, "quantity");
        if (stop > 0 && entry > stop)
            return (entry - stop) * qty;
        return 0m;
    }

    private static decimal GetDecimal(BsonDocument doc, string field)
    {
        if (!doc.Contains(field)) return 0m;
        var val = doc[field];
        if (val.IsDecimal128) return (decimal)val.AsDecimal128;
        if (val.IsDouble) return (decimal)val.AsDouble;
        if (val.IsInt32) return val.AsInt32;
        if (val.IsInt64) return val.AsInt64;
        return 0m;
    }

    // ── Freshness helpers (P6-T28 / REQ-DASH-012, T-9) ───────────────────────

    /// <summary>LADS completion timestamp from fyers_account_sync.</summary>
    private async Task<DateTime?> ReadLadsCompletionAsync(
        string userId, CancellationToken ct)
    {
        var sync = _database.GetCollection<BsonDocument>("fyers_account_sync");
        var doc = await sync
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId))
            .FirstOrDefaultAsync(ct);

        if (doc is null) return null;

        var val = doc.GetValue("last_successful_sync_at", BsonNull.Value);
        return val.IsBsonNull ? null : (DateTime?)val.ToUniversalTime();
    }

    /// <summary>
    /// LMDS price update timestamp = most recent <c>updated_at</c> across
    /// all non-terminal positions for this user (proxy for when LMDS last
    /// pushed a price update into any active position).
    /// </summary>
    private async Task<DateTime?> ReadLmdsPriceUpdateAsync(
        string userId, CancellationToken ct)
    {
        var positions = _database.GetCollection<BsonDocument>("positions");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId)
            & Builders<BsonDocument>.Filter.In("state",
                ["PendingEntry", "Open", "Suspended"]);

        var sort = Builders<BsonDocument>.Sort.Descending("updated_at");
        var latest = await positions
            .Find(filter)
            .Sort(sort)
            .Limit(1)
            .Project(Builders<BsonDocument>.Projection.Include("updated_at"))
            .FirstOrDefaultAsync(ct);

        if (latest is null) return null;

        var val = latest.GetValue("updated_at", BsonNull.Value);
        return val.IsBsonNull ? null : (DateTime?)val.ToUniversalTime();
    }

    /// <summary>Equity-base sync timestamp from the latest equity_curve entry.</summary>
    private async Task<DateTime?> ReadEquityBaseSyncAsync(
        string userId, CancellationToken ct)
    {
        var equityCurve = _database.GetCollection<BsonDocument>("equity_curve");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var sort = Builders<BsonDocument>.Sort.Descending("session_date");

        var latest = await equityCurve
            .Find(filter)
            .Sort(sort)
            .Limit(1)
            .Project(Builders<BsonDocument>.Projection.Include("session_date"))
            .FirstOrDefaultAsync(ct);

        if (latest is null) return null;

        var val = latest.GetValue("session_date", BsonNull.Value);
        if (val.IsBsonNull) return null;

        // session_date is stored as DateTime, convert to UTC
        return val.ToUniversalTime();
    }
}
