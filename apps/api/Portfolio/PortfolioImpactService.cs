using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Portfolio;

/// <summary>
/// Portfolio impact data — heat, sector exposure, open risk, entry status.
/// Returned by GET /api/v1/portfolio/impact (P6-T22).
/// </summary>
public sealed record PortfolioImpactResult(
    decimal PortfolioHeatPct,
    decimal MaxHeatPct,
    decimal TotalOpenRisk,
    decimal AccountEquity,
    int OpenPositionCount,
    int SuspendedPositionCount,
    bool EntryBlocked,
    string? EntryBlockedReason,
    IReadOnlyList<SectorExposureItem>? SectorExposures
);

/// <summary>
/// Per-sector exposure breakdown.
/// </summary>
public sealed record SectorExposureItem(
    string Sector,
    decimal ExposurePct,
    decimal OpenRisk
);

/// <summary>
/// Computes pre-trade portfolio impact (heat, sector exposure, entry status)
/// by reading positions and equity from MongoDB directly.
/// Used by the pre-trade portfolio impact panel on the chart page (P6-T22).
/// </summary>
public sealed class PortfolioImpactService
{
    private readonly IMongoDatabase _database;
    private readonly ISysConfigRepository _sysConfig;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PortfolioImpactService(
        IMongoDatabase database,
        ISysConfigRepository sysConfig)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _sysConfig = sysConfig ?? throw new ArgumentNullException(nameof(sysConfig));
    }

    /// <summary>
    /// Compute the pre-trade portfolio impact for the given user.
    /// Returns heat, sector exposure, and whether a new entry would be blocked.
    /// </summary>
    public async Task<PortfolioImpactResult> ComputeImpactAsync(
        string userId,
        CancellationToken ct = default)
    {
        // ── 1. Read equity ──────────────────────────────────────────────────
        var equity = await ReadEquityAsync(userId, ct);

        // ── 2. Read non-terminal positions ──────────────────────────────────
        var positions = await ReadNonTerminalPositionsAsync(userId, ct);

        var openPositions = positions.Where(p => p.State is "Open" or "PendingEntry").ToList();
        var suspendedPositions = positions.Where(p => p.State == "Suspended").ToList();
        var allActive = openPositions.Concat(suspendedPositions).ToList();

        // ── 3. Compute total open risk ──────────────────────────────────────
        var totalOpenRisk = allActive
            .Sum(p => p.StopLoss > 0 && p.EntryPrice > p.StopLoss
                ? (p.EntryPrice - p.StopLoss) * p.Quantity
                : 0m);

        // ── 4. Read max heat from sys_config ────────────────────────────────
        var maxHeatPct = await _sysConfig.GetDecimalAsync(
            "risk.default.max_portfolio_heat_pct", 5m, ct);

        // ── 5. Compute portfolio heat ───────────────────────────────────────
        var portfolioHeatPct = equity > 0
            ? Math.Round(totalOpenRisk / equity * 100m, 2)
            : 0m;

        // ── 6. Sector exposure ──────────────────────────────────────────────
        var sectorExposures = await ComputeSectorExposuresAsync(
            allActive, equity, ct);

        // ── 7. Entry blocked? ───────────────────────────────────────────────
        var entryBlocked = portfolioHeatPct >= maxHeatPct;
        var entryBlockedReason = entryBlocked
            ? $"Portfolio heat ({portfolioHeatPct}%) is at or above the maximum ({maxHeatPct}%). "
              + "Reduce open positions or increase the heat limit before entering a new trade."
            : null;

        return new PortfolioImpactResult(
            PortfolioHeatPct: portfolioHeatPct,
            MaxHeatPct: maxHeatPct,
            TotalOpenRisk: totalOpenRisk,
            AccountEquity: equity,
            OpenPositionCount: openPositions.Count,
            SuspendedPositionCount: suspendedPositions.Count,
            EntryBlocked: entryBlocked,
            EntryBlockedReason: entryBlockedReason,
            SectorExposures: sectorExposures
        );
    }

    /// <summary>
    /// Read the latest equity value from the equity_curve collection.
    /// </summary>
    private async Task<decimal> ReadEquityAsync(string userId, CancellationToken ct)
    {
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
            var equityValue = latest.GetValue("equity_value", BsonNull.Value);
            if (!equityValue.IsBsonNull && equityValue.IsDecimal128)
                return (decimal)equityValue.AsDecimal128;
        }

        // Fallback: portfolio_snapshots
        var snapshots = _database.GetCollection<BsonDocument>("portfolio_snapshots");
        var snapFilter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var snapshot = await snapshots.Find(snapFilter).FirstOrDefaultAsync(ct);

        if (snapshot is not null)
        {
            var mv = snapshot.GetValue("current_market_value", BsonNull.Value);
            var cash = snapshot.GetValue("cash_reserve", BsonNull.Value);
            var mvVal = mv.IsBsonNull || !mv.IsDecimal128 ? 0m : (decimal)mv.AsDecimal128;
            var cashVal = cash.IsBsonNull || !cash.IsDecimal128 ? 0m : (decimal)cash.AsDecimal128;
            return mvVal + cashVal;
        }

        return 0m;
    }

    /// <summary>
    /// Read non-terminal positions (Open, PendingEntry, Suspended) for the user.
    /// </summary>
    private async Task<List<PositionBrief>> ReadNonTerminalPositionsAsync(
        string userId, CancellationToken ct)
    {
        var positions = _database.GetCollection<BsonDocument>("positions");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId)
            & Builders<BsonDocument>.Filter.In("state",
                ["Open", "PendingEntry", "Suspended"]);

        var docs = await positions
            .Find(filter)
            .Project<BsonDocument>(Builders<BsonDocument>.Projection
                .Include("symbol")
                .Include("entry_price")
                .Include("quantity")
                .Include("stop_loss")
                .Include("state"))
            .ToListAsync(ct);

        return docs.Select(d => new PositionBrief(
            Symbol: d.GetValue("symbol", "").AsString,
            EntryPrice: d.GetValue("entry_price", 0m).IsDecimal128
                ? (decimal)d["entry_price"].AsDecimal128 : 0m,
            Quantity: d.GetValue("quantity", 0m).IsDecimal128
                ? (decimal)d["quantity"].AsDecimal128 : 0m,
            StopLoss: d.GetValue("stop_loss", 0m).IsDecimal128
                ? (decimal)d["stop_loss"].AsDecimal128 : 0m,
            State: d.GetValue("state", "").AsString
        )).ToList();
    }

    /// <summary>
    /// Compute sector exposure using the correlated-industry groups from sys_config.
    /// Symbols not matching any group fall under "Other".
    /// </summary>
    private async Task<IReadOnlyList<SectorExposureItem>> ComputeSectorExposuresAsync(
        List<PositionBrief> positions,
        decimal equity,
        CancellationToken ct)
    {
        if (positions.Count == 0 || equity <= 0)
            return [];

        // Read correlated industry groups from sys_config
        var groups = await ReadCorrelatedIndustryGroupsAsync(ct);

        // Read symbol → industry mapping
        var symbols = positions.Select(p => p.Symbol).Distinct().ToList();
        var industryMap = await ReadIndustryBySymbolAsync(symbols, ct);

        // Build reverse lookup: industry → group name
        var industryToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var ind in group.IndustryNames)
            {
                industryToGroup[ind] = group.GroupName;
            }
        }

        // Compute open risk per position
        var positionRisk = positions.Select(p => new
        {
            p.Symbol,
            Risk = p.StopLoss > 0 && p.EntryPrice > p.StopLoss
                ? (p.EntryPrice - p.StopLoss) * p.Quantity
                : 0m,
            Industry = industryMap.GetValueOrDefault(p.Symbol, "Other")
        }).ToList();

        // Group by sector (correlated group name, or "Other")
        var sectorRisk = positionRisk
            .GroupBy(p => industryToGroup.GetValueOrDefault(p.Industry, "Other"))
            .Select(g => new SectorExposureItem(
                Sector: g.Key,
                ExposurePct: Math.Round(g.Sum(x => x.Risk) / equity * 100m, 2),
                OpenRisk: g.Sum(x => x.Risk)
            ))
            .OrderByDescending(s => s.ExposurePct)
            .ToList();

        return sectorRisk;
    }

    /// <summary>
    /// Read and deserialize the correlated-industry groups from sys_config.
    /// Returns default groups when the config is absent or malformed.
    /// </summary>
    private async Task<IReadOnlyList<CorrelatedIndustryGroupRecord>> ReadCorrelatedIndustryGroupsAsync(
        CancellationToken ct)
    {
        var defaultGroups = new List<CorrelatedIndustryGroupRecord>
        {
            new("Banking & Finance", ["Banking", "Finance"]),
            new("Metals", ["Metals", "Metal Products"]),
            new("Oil & Gas", ["Oil", "Gas"])
        };

        try
        {
            var doc = await _sysConfig.GetByKeyAsync("risk.correlated_industry_groups", ct);
            if (doc is null || !doc.Contains("value"))
                return defaultGroups;

            var raw = doc["value"];
            var json = raw.IsString ? raw.AsString : raw.ToJson();
            if (string.IsNullOrWhiteSpace(json))
                return defaultGroups;

            var parsed = JsonSerializer.Deserialize<List<CorrelatedIndustryGroupRecord>>(json, JsonOptions);
            return parsed?.Count > 0 ? parsed : defaultGroups;
        }
        catch
        {
            return defaultGroups;
        }
    }

    /// <summary>
    /// Read the industry for each symbol from the symbol_master collection.
    /// </summary>
    private async Task<Dictionary<string, string>> ReadIndustryBySymbolAsync(
        List<string> symbols, CancellationToken ct)
    {
        var master = _database.GetCollection<BsonDocument>("symbol_master");
        var filter = Builders<BsonDocument>.Filter.In("symbol", symbols);
        var docs = await master
            .Find(filter)
            .Project<BsonDocument>(Builders<BsonDocument>.Projection
                .Include("symbol")
                .Include("industry"))
            .ToListAsync(ct);

        return docs
            .Where(d => d.Contains("symbol") && d.Contains("industry"))
            .ToDictionary(
                d => d["symbol"].AsString,
                d => d["industry"].AsString,
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Internal carrier for position data read from MongoDB.
    /// </summary>
    private sealed record PositionBrief(
        string Symbol,
        decimal EntryPrice,
        decimal Quantity,
        decimal StopLoss,
        string State
    );
}

/// <summary>
/// A correlated-industry group record for deserialization from sys_config JSON.
/// </summary>
public sealed record CorrelatedIndustryGroupRecord(
    string GroupName,
    IReadOnlyList<string> IndustryNames
);
