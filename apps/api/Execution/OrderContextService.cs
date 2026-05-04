using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Execution;

/// <summary>
/// Order context data for the Phase 1 platform modal (P7-T4).
/// Provides RME-recommended sizing, portfolio heat projections, stop levels,
/// and market state info needed to render the confirmation modal.
/// REQ-ORDER-007, REQ-ORDER-008, REQ-ORDER-011.
/// </summary>
public sealed record OrderContextResult(
    string Symbol,
    string ActionType,            // "entry" | "add" | "reduce" | "exit"
    decimal? RecommendedQuantity,
    decimal? CurrentPrice,
    decimal? StopLevel,
    decimal? TrailingStopLevel,
    bool TrailingStopActive,
    decimal PortfolioHeatBeforePct,
    decimal PortfolioHeatAfterPct,
    decimal MaxHeatPct,
    decimal AccountEquity,
    decimal? DrawdownPct,
    bool EntryBlocked,
    string? EntryBlockedReason,
    bool IsMarketHalted,
    bool InMarketHours,
    string? MarketHoursMessage,
    string? CircuitLimitDirection, // "upper" | "lower" | null
    bool CircuitLimitActive,
    bool CircuitLimitStale,
    bool HasPosition,
    string? PositionState
);

public sealed class OrderContextService
{
    private readonly IMongoDatabase _database;
    private readonly ISysConfigRepository _sysConfig;

    public OrderContextService(IMongoDatabase database, ISysConfigRepository sysConfig)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _sysConfig = sysConfig ?? throw new ArgumentNullException(nameof(sysConfig));
    }

    public async Task<OrderContextResult> GetOrderContextAsync(
        string userId, string symbol, string actionType, CancellationToken ct)
    {
        var positions = _database.GetCollection<BsonDocument>("positions");
        var equityCurve = _database.GetCollection<BsonDocument>("equity_curve");
        var snapshots = _database.GetCollection<BsonDocument>("portfolio_snapshots");
        var haltCollection = _database.GetCollection<BsonDocument>("market_halt_state");

        // ── Position data ──────────────────────────────────────────────────
        BsonDocument? position = null;
        try
        {
            position = await positions
                .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                    & Builders<BsonDocument>.Filter.Eq("symbol", symbol)
                    & Builders<BsonDocument>.Filter.Ne("state", "Closed"))
                .FirstOrDefaultAsync(ct);
        }
        catch (MongoException) { /* position may not exist */ }

        var hasPosition = position is not null;
        var posState = position?.GetValue("state", BsonNull.Value) switch
        {
            BsonString s => s.Value,
            _ => null
        };
        var openQty = position?.GetValue("quantity", BsonNull.Value) switch
        {
            BsonDecimal128 d => (decimal)d.Value,
            BsonDouble d => (decimal)d.Value,
            BsonInt32 i => i.Value,
            BsonInt64 i => (decimal)i.Value,
            _ => 0m
        };
        var entryPrice = position?.GetValue("average_entry_price", BsonNull.Value) switch
        {
            BsonDecimal128 d => (decimal)d.Value,
            BsonDouble d => (decimal)d.Value,
            _ => (decimal?)null
        };
        var circuitActive = position?.GetValue("circuit_limit_active", BsonNull.Value) switch
        {
            BsonBoolean b => b.Value,
            _ => false
        };
        var circuitDirection = position?.GetValue("circuit_limit_direction", BsonNull.Value) switch
        {
            BsonString s => s.Value,
            _ => null
        };

        // ── Equity / heat ──────────────────────────────────────────────────
        var equity = await ReadEquityAsync(userId, equityCurve, snapshots, ct);
        var heatBeforePct = await ComputePortfolioHeatPctAsync(userId, equity, positions, ct);

        // ── Drawdown ───────────────────────────────────────────────────────
        var drawdownPct = await ComputeDrawdownPctAsync(userId, equity, equityCurve, ct);

        // ── Halt state ─────────────────────────────────────────────────────
        var isHalted = await IsMarketHaltedAsync(haltCollection, ct);

        // ── Market hours ───────────────────────────────────────────────────
        var (inHours, hoursMsg) = await CheckMarketHoursAsync(ct);

        // ── Circuit limit staleness ────────────────────────────────────────
        var circuitStaleMinutes = await _sysConfig.GetDecimalAsync(
            "risk.circuit_limit_stale_minutes", 15m, ct);
        var circuitStale = await IsCircuitLimitStaleAsync(position, circuitStaleMinutes, ct);

        // ── Entry blocked ──────────────────────────────────────────────────
        var (entryBlocked, entryBlockedReason) = await CheckEntryBlockedAsync(
            userId, drawdownPct, ct);

        // ── Stop level ─────────────────────────────────────────────────────
        var stopLevel = position?.GetValue("active_stop", BsonNull.Value) switch
        {
            BsonDecimal128 d => (decimal)d.Value,
            BsonDouble d => (decimal)d.Value,
            _ => (decimal?)null
        };
        var trailingStop = position?.GetValue("trailing_stop", BsonNull.Value) switch
        {
            BsonDecimal128 d => (decimal)d.Value,
            BsonDouble d => (decimal)d.Value,
            _ => (decimal?)null
        };
        var trailingActive = position?.GetValue("trailing_stop_active", BsonNull.Value) switch
        {
            BsonBoolean b => b.Value,
            _ => false
        };

        // ── Recommended quantity ───────────────────────────────────────────
        var recommendedQty = await ComputeRecommendedQuantityAsync(
            userId, symbol, actionType, position, openQty, equity, ct);

        // ── Portfolio heat projection ──────────────────────────────────────
        var currentPrice = 0m; // Frontend provides live price; backend estimates
        var heatAfterPct = await ProjectHeatAfterTradeAsync(
            userId, equity, heatBeforePct, recommendedQty, currentPrice, entryPrice, positions, ct);

        return new OrderContextResult(
            Symbol: symbol,
            ActionType: actionType,
            RecommendedQuantity: recommendedQty > 0 ? recommendedQty : null,
            CurrentPrice: null, // Frontend gets live price from WebSocket
            StopLevel: stopLevel,
            TrailingStopLevel: trailingStop,
            TrailingStopActive: trailingActive,
            PortfolioHeatBeforePct: Math.Round(heatBeforePct, 2),
            PortfolioHeatAfterPct: Math.Round(heatAfterPct, 2),
            MaxHeatPct: Math.Round(await ReadMaxHeatPctAsync(ct), 2),
            AccountEquity: equity,
            DrawdownPct: drawdownPct > 0 ? Math.Round(drawdownPct, 2) : null,
            EntryBlocked: entryBlocked,
            EntryBlockedReason: entryBlocked ? entryBlockedReason : null,
            IsMarketHalted: isHalted,
            InMarketHours: inHours,
            MarketHoursMessage: hoursMsg,
            CircuitLimitDirection: circuitActive ? circuitDirection : null,
            CircuitLimitActive: circuitActive && !circuitStale,
            CircuitLimitStale: circuitActive && circuitStale,
            HasPosition: hasPosition,
            PositionState: posState
        );
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private async Task<decimal> ReadEquityAsync(string userId,
        IMongoCollection<BsonDocument> equityCurve,
        IMongoCollection<BsonDocument> snapshots, CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var sort = Builders<BsonDocument>.Sort.Descending("session_date");
        var latest = await equityCurve.Find(filter).Sort(sort).Limit(1).FirstOrDefaultAsync(ct);

        if (latest is not null)
        {
            var val = latest.GetValue("equity_value", BsonNull.Value);
            if (!val.IsBsonNull && val.IsDecimal128)
                return (decimal)val.AsDecimal128;
        }

        var snap = await snapshots.Find(filter).FirstOrDefaultAsync(ct);
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

    private async Task<decimal> ComputePortfolioHeatPctAsync(string userId, decimal equity,
        IMongoCollection<BsonDocument> positions, CancellationToken ct)
    {
        if (equity <= 0) return 0m;

        var openPositions = await positions
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                & Builders<BsonDocument>.Filter.In("state", new[] { "Open", "Suspended" }))
            .ToListAsync(ct);

        var totalRisk = 0m;
        foreach (var pos in openPositions)
        {
            var risk = pos.GetValue("risk_amount", BsonNull.Value);
            if (!risk.IsBsonNull && risk.IsDecimal128)
                totalRisk += (decimal)risk.AsDecimal128;
        }

        return (totalRisk / equity) * 100m;
    }

    private async Task<decimal> ComputeDrawdownPctAsync(string userId, decimal equity,
        IMongoCollection<BsonDocument> equityCurve, CancellationToken ct)
    {
        if (equity <= 0) return 0m;

        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var sort = Builders<BsonDocument>.Sort.Descending("equity_value");
        var highest = await equityCurve.Find(filter).Sort(sort).Limit(1).FirstOrDefaultAsync(ct);

        if (highest is not null)
        {
            var val = highest.GetValue("equity_value", BsonNull.Value);
            if (!val.IsBsonNull && val.IsDecimal128)
            {
                var hwm = Math.Max((decimal)val.AsDecimal128, equity);
                if (hwm > 0 && equity < hwm)
                    return (hwm - equity) / hwm * 100m;
            }
        }

        return 0m;
    }

    private async Task<bool> IsMarketHaltedAsync(
        IMongoCollection<BsonDocument> haltCollection, CancellationToken ct)
    {
        try
        {
            var doc = await haltCollection
                .Find(Builders<BsonDocument>.Filter.Empty)
                .FirstOrDefaultAsync(ct);
            return doc is not null
                && doc.Contains("halt_active")
                && doc["halt_active"] is BsonBoolean hb
                && hb.Value;
        }
        catch (MongoException) { return false; }
    }

    private async Task<(bool InHours, string? Message)> CheckMarketHoursAsync(CancellationToken ct)
    {
        try
        {
            var calendar = _database.GetCollection<BsonDocument>("trading_calendar");
            var now = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(
                DateTime.UtcNow, "India Standard Time");
            var today = now.Date;

            var todayEntry = await calendar
                .Find(Builders<BsonDocument>.Filter.Eq("date", today.ToString("yyyy-MM-dd")))
                .FirstOrDefaultAsync(ct);

            if (todayEntry is null)
                return (false, "No trading session defined for today. Orders will be subject to FYERS AMO rules.");

            var isTradingDay = todayEntry.GetValue("is_trading_day", BsonNull.Value) switch
            {
                BsonBoolean b => b.Value,
                _ => true
            };
            if (!isTradingDay)
                return (false, "Today is not a trading day. Orders will be subject to FYERS AMO rules.");

            var sessionStart = todayEntry.GetValue("session_start", BsonNull.Value) switch
            {
                BsonString s => ParseIstTime(s.Value, today),
                _ => today.AddHours(9).AddMinutes(15) // Default 09:15 IST
            };
            var sessionEnd = todayEntry.GetValue("session_end", BsonNull.Value) switch
            {
                BsonString s => ParseIstTime(s.Value, today),
                _ => today.AddHours(15).AddMinutes(30) // Default 15:30 IST
            };

            if (now < sessionStart)
                return (false, "Market opens at 09:15 IST. Orders placed before open will be subject to FYERS pre-market / AMO rules.");
            if (now > sessionEnd)
                return (false, "Market closed at 15:30 IST. Orders placed after close will be subject to FYERS AMO rules.");

            return (true, null);
        }
        catch
        {
            return (true, null); // Default to allowing if calendar is unavailable
        }
    }

    private static DateTime ParseIstTime(string time, DateTime date)
    {
        var parts = time.Split(':');
        if (parts.Length >= 2
            && int.TryParse(parts[0], out var h)
            && int.TryParse(parts[1], out var m))
        {
            return date.AddHours(h).AddMinutes(m);
        }
        return date.AddHours(9).AddMinutes(15);
    }

    private async Task<bool> IsCircuitLimitStaleAsync(
        BsonDocument? position, decimal staleMinutes, CancellationToken ct)
    {
        if (position is null) return false;
        var updatedAt = position.GetValue("updated_at", BsonNull.Value) switch
        {
            BsonDateTime dt => dt.ToUniversalTime(),
            _ => (DateTime?)null
        };
        if (updatedAt is null) return true;
        return (DateTime.UtcNow - updatedAt.Value).TotalMinutes > (double)staleMinutes;
    }

    private async Task<(bool Blocked, string? Reason)> CheckEntryBlockedAsync(
        string userId, decimal drawdownPct, CancellationToken ct)
    {
        try
        {
            // Kill switch check
            var sysConfigColl = _database.GetCollection<BsonDocument>("sys_config");
            var ksDoc = await sysConfigColl
                .Find(Builders<BsonDocument>.Filter.Eq("key", "risk.kill_switch.active"))
                .FirstOrDefaultAsync(ct);
            if (ksDoc is not null
                && ksDoc.Contains("value")
                && ksDoc["value"] is BsonBoolean kb
                && kb.Value)
                return (true, "Global kill switch is active. All trading operations are suspended.");

            // Drawdown suppression
            var suppressEntryPct = await _sysConfig.GetDecimalAsync(
                "risk.drawdown_enforcement.suppress_entry_pct", 20m, ct);
            if (drawdownPct >= suppressEntryPct)
                return (true, $"New entries are suppressed — drawdown at {drawdownPct:F1}% (threshold: {suppressEntryPct}%).");
        }
        catch { /* If checks fail, default to allowing */ }

        return (false, null);
    }

    private async Task<decimal> ComputeRecommendedQuantityAsync(
        string userId, string symbol, string actionType,
        BsonDocument? position, decimal openQty, decimal equity, CancellationToken ct)
    {
        switch (actionType.ToLowerInvariant())
        {
            case "exit":
                // Full exit: recommend full open quantity
                // Partial exit: recommend advised reduce quantity
                if (position is not null && openQty > 0)
                {
                    var exitAdvisoryQty = position.GetValue("exit_advisory_quantity", BsonNull.Value) switch
                    {
                        BsonDecimal128 d => (decimal)d.Value,
                        _ => (decimal?)null
                    };
                    return exitAdvisoryQty ?? openQty;
                }
                return 0;

            case "reduce":
                if (position is not null && openQty > 0)
                {
                    var reduceAdvisoryQty = position.GetValue("reduce_advisory_quantity", BsonNull.Value) switch
                    {
                        BsonDecimal128 d => (decimal)d.Value,
                        _ => (decimal?)null
                    };
                    // If no advisory, default to 1/3 of position
                    return reduceAdvisoryQty ?? Math.Max(1, Math.Round(openQty / 3m));
                }
                return 0;

            case "add":
                if (position is not null && openQty > 0)
                {
                    var addAdvisoryQty = position.GetValue("add_advisory_quantity", BsonNull.Value) switch
                    {
                        BsonDecimal128 d => (decimal)d.Value,
                        _ => (decimal?)null
                    };
                    // If no advisory, default to current position size
                    return addAdvisoryQty ?? openQty;
                }
                return 0;

            case "entry":
                // Default entry sizing: 0.5% risk per trade
                if (equity > 0)
                {
                    var riskPct = await _sysConfig.GetDecimalAsync(
                        "position_sizing.default_risk_per_trade_pct", 0.5m, ct);
                    var riskAmount = equity * (riskPct / 100m);
                    // Without a current price, we return the risk amount.
                    // The frontend estimates quantity from price.
                    // Round to nearest integer quantity.
                    var estQty = riskAmount > 0 ? Math.Max(1, Math.Round(riskAmount / 1000m)) : 1;
                    return estQty;
                }
                return 1;

            default:
                return 0;
        }
    }

    private async Task<decimal> ProjectHeatAfterTradeAsync(
        string userId, decimal equity, decimal heatBeforePct,
        decimal qty, decimal price, decimal? avgPrice,
        IMongoCollection<BsonDocument> positions, CancellationToken ct)
    {
        if (equity <= 0 || qty <= 0 || price <= 0)
            return heatBeforePct;

        // Estimate additional risk from this trade
        var riskPerShare = avgPrice.HasValue && avgPrice.Value > 0
            ? Math.Abs(price - avgPrice.Value)
            : price * 0.05m; // Default 5% assumed risk if no entry price

        var additionalRisk = qty * riskPerShare;
        var totalRisk = (heatBeforePct / 100m) * equity + additionalRisk;

        return (totalRisk / equity) * 100m;
    }

    private async Task<decimal> ReadMaxHeatPctAsync(CancellationToken ct)
    {
        try
        {
            return await _sysConfig.GetDecimalAsync(
                "risk.default.max_portfolio_heat_pct", 6m, ct);
        }
        catch { return 6m; }
    }
}
