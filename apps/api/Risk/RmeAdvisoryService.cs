using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Risk;

/// <summary>
/// RME advisory data for a single symbol on the chart page.
/// P6-T27 / REQ-CHART-* (advisory subset).
/// </summary>
public sealed record RmeAdvisoryResult(
    bool PositionExists,
    string? State,
    decimal? EntryPrice,
    decimal? AverageEntryPrice,
    decimal? Quantity,
    decimal? StopLoss,
    decimal? ActiveStop,
    decimal? TrailingStop,
    bool TrailingStopActive,
    decimal? CurrentRMultiple,
    decimal? UnrealizedPnl,
    decimal? RiskAmount,
    bool AddAdvisoryActive,
    bool ReduceAdvisoryActive,
    bool ExitAdvisoryActive,
    decimal? AddLevel,
    decimal? ReduceLevel,
    string? TimeStopDate,
    int OpenPositionCount,
    string? SubscriptionName
);

/// <summary>
/// Reads per-symbol position data from MongoDB and returns the RME advisory
/// data displayed in the chart page advisory panel. P6-T27.
/// </summary>
public sealed class RmeAdvisoryService
{
    private readonly IMongoDatabase _database;

    public RmeAdvisoryService(IMongoDatabase database)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
    }

    /// <summary>
    /// Compute RME advisory for the given user and symbol.
    /// </summary>
    public async Task<RmeAdvisoryResult> ComputeAdvisoryAsync(
        string userId,
        string symbol,
        CancellationToken ct = default)
    {
        var positions = _database.GetCollection<BsonDocument>("positions");

        // ── 1. Find an active position for this user + symbol ──────────────
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId)
            & Builders<BsonDocument>.Filter.Eq("symbol", symbol);

        var position = await positions
            .Find(filter)
            .Sort(Builders<BsonDocument>.Sort.Descending("updated_at"))
            .FirstOrDefaultAsync(ct);

        // ── 2. Count all non-terminal positions (Open, PendingEntry, Suspended) ──
        var openCount = await positions
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                & Builders<BsonDocument>.Filter.In("state",
                    ["Open", "PendingEntry", "Suspended"]))
            .CountDocumentsAsync(ct);

        if (position is null)
        {
            return new RmeAdvisoryResult(
                PositionExists: false,
                State: null,
                EntryPrice: null,
                AverageEntryPrice: null,
                Quantity: null,
                StopLoss: null,
                ActiveStop: null,
                TrailingStop: null,
                TrailingStopActive: false,
                CurrentRMultiple: null,
                UnrealizedPnl: null,
                RiskAmount: null,
                AddAdvisoryActive: false,
                ReduceAdvisoryActive: false,
                ExitAdvisoryActive: false,
                AddLevel: null,
                ReduceLevel: null,
                TimeStopDate: null,
                OpenPositionCount: (int)openCount,
                SubscriptionName: null
            );
        }

        var entryPrice = GetDecimal(position, "entry_price");
        var quantity = GetDecimal(position, "quantity");
        var stopLoss = GetDecimal(position, "stop_loss");
        var trailingStop = GetDecimal(position, "trailing_stop");
        var currentPrice = GetDecimal(position, "current_price");
        var trailingStopActive = position.GetValue("trailing_stop_active", false).AsBoolean;
        var addAdvisory = position.GetValue("add_advisory_active", false).AsBoolean;
        var reduceAdvisory = position.GetValue("reduce_advisory_active", false).AsBoolean;
        var exitAdvisory = position.GetValue("exit_advisory_active", false).AsBoolean;
        var state = position.GetValue("state", "").AsString;
        var averageEntryPrice = GetDecimal(position, "average_entry_price");
        var timeStopDate = position.Contains("time_stop_date")
            ? position["time_stop_date"].AsString
            : null;

        // ── 3. Compute derived values ─────────────────────────────────────
        // Ensure non-zero for division safety
        var riskPerShare = stopLoss > 0 && entryPrice > stopLoss
            ? entryPrice - stopLoss
            : 0m;

        var riskAmount = riskPerShare > 0 && quantity > 0
            ? riskPerShare * quantity
            : 0m;

        var unrealizedPnl = quantity > 0 && currentPrice > 0 && entryPrice > 0
            ? (currentPrice - entryPrice) * quantity
            : 0m;

        var currentRMultiple = riskPerShare > 0 && unrealizedPnl > 0
            ? Math.Round(unrealizedPnl / (riskPerShare * quantity), 2)
            : 0m;

        // Active stop is the trailing_stop when trailing is active, else stop_loss
        decimal? activeStop;
        if (trailingStopActive && trailingStop > 0)
            activeStop = trailingStop;
        else if (stopLoss > 0)
            activeStop = stopLoss;
        else
            activeStop = null;

        // ── 4. Determine add/reduce levels ────────────────────────────────
        // Add level: typically a configurable % above entry. Default: 5%.
        // For now, read it from the position or use a heuristic.
        // In a full implementation, the RME computes these from the pyradming config.
        decimal? addLevel = null;
        if (averageEntryPrice > 0)
        {
            // If the add_advisory is active, compute a reference level
            // The actual add level is determined by the RME; we use entry_price + 5% as a fallback display
            addLevel = Math.Round(averageEntryPrice * 1.05m, 2);
        }

        decimal? reduceLevel = null;
        if (averageEntryPrice > 0)
        {
            reduceLevel = Math.Round(averageEntryPrice * 1.15m, 2);
        }

        // ── 5. Subscription name from signal_subscription_name or entry signal provenance ──
        var subscriptionName = position.Contains("signal_subscription_name")
            ? position["signal_subscription_name"].AsString
            : position.Contains("subscription_name")
                ? position["subscription_name"].AsString
                : null;

        // Use average_entry_price if set, else entry_price
        var avgEntry = averageEntryPrice > 0 ? averageEntryPrice : entryPrice;

        return new RmeAdvisoryResult(
            PositionExists: true,
            State: state,
            EntryPrice: entryPrice > 0 ? entryPrice : null,
            AverageEntryPrice: avgEntry > 0 ? avgEntry : null,
            Quantity: quantity > 0 ? quantity : null,
            StopLoss: stopLoss > 0 ? stopLoss : null,
            ActiveStop: activeStop,
            TrailingStop: trailingStop > 0 ? trailingStop : null,
            TrailingStopActive: trailingStopActive,
            CurrentRMultiple: currentRMultiple,
            UnrealizedPnl: unrealizedPnl,
            RiskAmount: riskAmount > 0 ? riskAmount : null,
            AddAdvisoryActive: addAdvisory,
            ReduceAdvisoryActive: reduceAdvisory,
            ExitAdvisoryActive: exitAdvisory,
            AddLevel: addLevel,
            ReduceLevel: reduceLevel,
            TimeStopDate: timeStopDate,
            OpenPositionCount: (int)openCount,
            SubscriptionName: subscriptionName
        );
    }

    private static decimal GetDecimal(BsonDocument doc, string field)
    {
        if (!doc.Contains(field))
            return 0m;

        var val = doc[field];
        if (val.IsDecimal128)
            return (decimal)val.AsDecimal128;
        if (val.IsDouble)
            return (decimal)val.AsDouble;
        if (val.IsInt32)
            return val.AsInt32;
        if (val.IsInt64)
            return val.AsInt64;
        return 0m;
    }
}
