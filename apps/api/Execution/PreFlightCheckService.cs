using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Fyers;
using SignalStack.Storage.Fyers;
using SignalStack.Storage.SysConfig;

namespace SignalStack.Api.Execution;

public sealed record PreFlightCheckResult(
    bool CanExecute,
    IReadOnlyList<CheckResult> Checks
);

public sealed record CheckResult(
    string Check,
    bool Passed,
    string Severity, // "blocking" | "warning"
    string? Message
);

/// <summary>
/// Pre-flight checks for execution assistance (P7-T3).
/// Validates token, kill switch, drawdown, duplicates, halt state, and LADS health
/// before allowing a user to proceed to the FYERS execution widget.
/// </summary>
public sealed class PreFlightCheckService
{
    private readonly IMongoDatabase _database;
    private readonly ISysConfigRepository _sysConfig;
    private readonly IFyersTokenRepository _fyersTokenRepo;

    public PreFlightCheckService(
        IMongoDatabase database,
        ISysConfigRepository sysConfig,
        IFyersTokenRepository fyersTokenRepo)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _sysConfig = sysConfig ?? throw new ArgumentNullException(nameof(sysConfig));
        _fyersTokenRepo = fyersTokenRepo ?? throw new ArgumentNullException(nameof(fyersTokenRepo));
    }

    public async Task<PreFlightCheckResult> CheckAsync(
        string userId, string? symbol, CancellationToken ct)
    {
        var checks = new List<CheckResult>(6);

        checks.Add(await CheckTokenValidityAsync(userId, ct));
        checks.Add(await CheckKillSwitchAsync(ct));
        checks.Add(await CheckDrawdownEnforcementAsync(userId, ct));
        checks.Add(await CheckDuplicateInFlightAsync(userId, symbol, ct));
        checks.Add(await CheckHaltStateAsync(ct));
        checks.Add(await CheckLadsSustainedFailureAsync(ct));

        var blocking = checks.Count(c => !c.Passed && c.Severity == "blocking");

        return new PreFlightCheckResult(
            CanExecute: blocking == 0,
            Checks: checks.AsReadOnly()
        );
    }

    /// <summary>Check 1 — The user's FYERS token is active and not expired/dirty.</summary>
    private async Task<CheckResult> CheckTokenValidityAsync(string userId, CancellationToken ct)
    {
        var hasActive = await _fyersTokenRepo.HasActiveTokenAsync(userId, ct);
        return new CheckResult(
            "token_valid",
            hasActive,
            "blocking",
            hasActive ? null : "FYERS token is missing, expired, or marked dirty. Re-link your FYERS account."
        );
    }

    /// <summary>Check 2 — The global RME kill switch is not active.</summary>
    private async Task<CheckResult> CheckKillSwitchAsync(CancellationToken ct)
    {
        var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
        var doc = await sysConfig
            .Find(Builders<BsonDocument>.Filter.Eq("key", "risk.kill_switch.active"))
            .FirstOrDefaultAsync(ct);

        var isActive = doc is not null
            && doc.Contains("value")
            && doc["value"] is BsonBoolean b
            && b.Value;

        return new CheckResult(
            "kill_switch",
            !isActive,
            "blocking",
            isActive ? "Global kill switch is active. All trading operations are suspended by an administrator." : null
        );
    }

    /// <summary>
    /// Check 3 — Drawdown enforcement per REQ-DRDN-003 Category (a).
    /// At suppress_entry_pct (default 20%) new entries are blocked.
    /// At size_reduction_pct (default 5%) a non-blocking advisory is issued.
    /// </summary>
    private async Task<CheckResult> CheckDrawdownEnforcementAsync(string userId, CancellationToken ct)
    {
        var equity = await ReadEquityAsync(userId, ct);
        if (equity <= 0)
        {
            return new CheckResult(
                "drawdown_enforcement",
                false,
                "blocking",
                "Account equity is zero or negative. Unable to verify drawdown limits."
            );
        }

        var hwm = await ReadHighWaterMarkAsync(userId, equity, ct);
        var drawdownPct = hwm > 0 && equity < hwm
            ? Math.Round((hwm - equity) / hwm * 100m, 2)
            : 0m;

        var suppressEntryPct = await _sysConfig.GetDecimalAsync(
            "risk.drawdown_enforcement.suppress_entry_pct", 20m, ct);

        if (drawdownPct >= suppressEntryPct)
        {
            return new CheckResult(
                "drawdown_enforcement",
                false,
                "blocking",
                $"Drawdown at {drawdownPct}% — new entries are suppressed (threshold: {suppressEntryPct}%)."
            );
        }

        var sizeReductionPct = await _sysConfig.GetDecimalAsync(
            "risk.drawdown_enforcement.size_reduction_pct", 5m, ct);

        if (drawdownPct >= sizeReductionPct)
        {
            return new CheckResult(
                "drawdown_enforcement",
                true,
                "warning",
                $"Drawdown at {drawdownPct}% — position sizing is reduced (threshold: {sizeReductionPct}%)."
            );
        }

        return new CheckResult("drawdown_enforcement", true, "blocking", null);
    }

    /// <summary>
    /// Check 4 — No duplicate entry in flight for the same symbol.
    /// Checks the positions collection for an existing PendingEntry position
    /// for this user + symbol combination.
    /// </summary>
    private async Task<CheckResult> CheckDuplicateInFlightAsync(string userId, string? symbol, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return new CheckResult("duplicate_in_flight", true, "blocking", null);
        }

        var positions = _database.GetCollection<BsonDocument>("positions");

        var existing = await positions
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                & Builders<BsonDocument>.Filter.Eq("symbol", symbol)
                & Builders<BsonDocument>.Filter.Eq("state", "PendingEntry"))
            .FirstOrDefaultAsync(ct);

        if (existing is not null)
        {
            return new CheckResult(
                "duplicate_in_flight",
                false,
                "blocking",
                $"An entry for {symbol} is already being processed."
            );
        }

        return new CheckResult("duplicate_in_flight", true, "blocking", null);
    }

    /// <summary>
    /// Check 5 — Market halt state per REQ-HALT-005.
    /// Non-blocking warning — the user may still submit despite a halt.
    /// Reads from the market_halt_state collection written by LMDS.
    /// Returns passed=true when no halt is detected; also passed=true
    /// (with a warning message) when halted, because halt is non-blocking.
    /// </summary>
    private async Task<CheckResult> CheckHaltStateAsync(CancellationToken ct)
    {
        var haltCollection = _database.GetCollection<BsonDocument>("market_halt_state");
        BsonDocument? haltDoc = null;

        try
        {
            haltDoc = await haltCollection
                .Find(Builders<BsonDocument>.Filter.Empty)
                .FirstOrDefaultAsync(ct);
        }
        catch (MongoException)
        {
            // Collection may not exist yet — no halt state to report.
        }

        var isHalted = haltDoc is not null
            && haltDoc.Contains("halt_active")
            && haltDoc["halt_active"] is BsonBoolean hb
            && hb.Value;

        if (isHalted)
        {
            return new CheckResult(
                "halt_state",
                true, // Non-blocking per REQ-HALT-005
                "warning",
                "Market is halted. FYERS is likely to reject the order. You may still proceed."
            );
        }

        return new CheckResult("halt_state", true, "warning", null);
    }

    /// <summary>
    /// Check 6 — LADS sustained-failure per RME-R2.
    /// Checks rme_incidents for an open lads_sustained_failure incident.
    /// When active, account data may be stale and new entries are blocked.
    /// </summary>
    private async Task<CheckResult> CheckLadsSustainedFailureAsync(CancellationToken ct)
    {
        var incidents = _database.GetCollection<BsonDocument>("rme_incidents");

        var failure = await incidents
            .Find(Builders<BsonDocument>.Filter.Eq("incident_type", "lads_sustained_failure")
                & Builders<BsonDocument>.Filter.Eq("status", "open"))
            .FirstOrDefaultAsync(ct);

        if (failure is not null)
        {
            return new CheckResult(
                "lads_sustained_failure",
                false,
                "blocking",
                "LADS (Live Account Data Sync) has a sustained failure. Account data may be stale. New entries are blocked until the issue is resolved."
            );
        }

        return new CheckResult("lads_sustained_failure", true, "blocking", null);
    }

    // ── Equity helpers (consistent with PortfolioHealthService) ──────────

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
            var val = latest.GetValue("equity_value", BsonNull.Value);
            if (!val.IsBsonNull && val.IsDecimal128)
                return (decimal)val.AsDecimal128;
        }

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
}
