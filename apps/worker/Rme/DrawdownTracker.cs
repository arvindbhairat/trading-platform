using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Tracks account-level drawdown from the equity high-water mark using the
/// append-only <c>equity_curve</c> collection. Evaluates the six graduated
/// drawdown thresholds (REQ-DRDN-003) and loss-limit advisories (REQ-DRDN-002)
/// against configurable sys_config values (REQ-DRDN-004).
///
/// Thread-safe; register as singleton.
/// </summary>
internal sealed class DrawdownTracker : IDrawdownTracker
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<DrawdownTracker> _logger;

    private static readonly Counter<long> AssessTotal = WorkerTelemetry.Meter
        .CreateCounter<long>("rme.drawdown.assess_total",
            description: "Drawdown assessment calls, tagged by outcome. REQ-DRDN-001.");

    private static readonly Counter<long> ThresholdBreachTotal = WorkerTelemetry.Meter
        .CreateCounter<long>("rme.drawdown.threshold_breach_total",
            description: "Drawdown threshold breach events, tagged by level. REQ-DRDN-003.");

    // ── Equity curve read constants ─────────────────────────────────────────
    private const int MaxEquityRecords = 252;

    // ── Sys_config key constants (REQ-DRDN-004 naming convention) ───────────

    // Advisory loss-limit keys (category b — REQ-DRDN-002)
    private const string DailyLossLimitKey = "risk.drawdown_advisory.daily_loss_limit_pct";
    private const string WeeklyLossLimitKey = "risk.drawdown_advisory.weekly_loss_limit_pct";
    private const string MonthlyLossLimitKey = "risk.drawdown_advisory.monthly_loss_limit_pct";

    // Enforcement threshold keys (REQ-DRDN-003)
    private const string SizeReductionKey = "risk.drawdown_enforcement.size_reduction_pct";
    private const string SuppressAddonKey = "risk.drawdown_enforcement.suppress_addon_pct";
    private const string AdvisoryReducePositionsKey = "risk.drawdown_enforcement.advisory_reduce_positions_pct";
    private const string SuppressEntryKey = "risk.drawdown_enforcement.suppress_entry_pct";
    private const string AdvisoryCloseWeakestKey = "risk.drawdown_enforcement.advisory_close_weakest_pct";
    private const string AdvisoryStopAllTradingKey = "risk.drawdown_enforcement.advisory_stop_all_trading_pct";

    // ── Default threshold values (REQ-DRDN-003) ─────────────────────────────
    private const decimal DefaultDailyLossLimitPct = 2m;
    private const decimal DefaultWeeklyLossLimitPct = 4m;
    private const decimal DefaultMonthlyLossLimitPct = 6m;
    private const decimal DefaultSizeReductionPct = 5m;
    private const decimal DefaultSuppressAddonPct = 10m;
    private const decimal DefaultAdvisoryReducePositionsPct = 15m;
    private const decimal DefaultSuppressEntryPct = 20m;
    private const decimal DefaultAdvisoryCloseWeakestPct = 25m;
    private const decimal DefaultAdvisoryStopAllTradingPct = 30m;

    public DrawdownTracker(
        IMongoDatabase database,
        ILogger<DrawdownTracker> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DrawdownAssessment> AssessAsync(string userId, CancellationToken ct = default)
    {
        // ── Step 1: Read equity curve history ───────────────────────────────
        var equityHistory = await ReadEquityHistoryAsync(userId, ct);

        if (equityHistory.Count == 0)
        {
            _logger.LogWarning(
                "DrawdownTracker: no equity_curve records found for user {UserId}. " +
                "Returning empty assessment.", userId);

            AssessTotal.Add(1, new KeyValuePair<string, object?>("outcome", "no_data"));
            return new DrawdownAssessment
            {
                AdvisoryMessage = "No equity curve data available for drawdown assessment.",
            };
        }

        // ── Step 2: Compute HWM and current drawdown ────────────────────────
        var latest = equityHistory[^1];
        var hwm = equityHistory.Max(e => e.Equity);
        var currentDrawdownPct = hwm > 0m
            ? Math.Round((hwm - latest.Equity) / hwm * 100m, 2)
            : 0m;

        if (currentDrawdownPct < 0m) currentDrawdownPct = 0m;

        // ── Step 3: Read all threshold values from sys_config ───────────────
        var dailyLimitPct = await ReadSysConfigDecimalAsync(DailyLossLimitKey, DefaultDailyLossLimitPct, ct);
        var weeklyLimitPct = await ReadSysConfigDecimalAsync(WeeklyLossLimitKey, DefaultWeeklyLossLimitPct, ct);
        var monthlyLimitPct = await ReadSysConfigDecimalAsync(MonthlyLossLimitKey, DefaultMonthlyLossLimitPct, ct);

        var sizeReductionThreshold = await ReadSysConfigDecimalAsync(SizeReductionKey, DefaultSizeReductionPct, ct);
        var suppressAddonThreshold = await ReadSysConfigDecimalAsync(SuppressAddonKey, DefaultSuppressAddonPct, ct);
        var advisoryReducePositionsThreshold = await ReadSysConfigDecimalAsync(AdvisoryReducePositionsKey, DefaultAdvisoryReducePositionsPct, ct);
        var suppressEntryThreshold = await ReadSysConfigDecimalAsync(SuppressEntryKey, DefaultSuppressEntryPct, ct);
        var advisoryCloseWeakestThreshold = await ReadSysConfigDecimalAsync(AdvisoryCloseWeakestKey, DefaultAdvisoryCloseWeakestPct, ct);
        var advisoryStopAllTradingThreshold = await ReadSysConfigDecimalAsync(AdvisoryStopAllTradingKey, DefaultAdvisoryStopAllTradingPct, ct);

        // ── Step 4: Evaluate loss limits (REQ-DRDN-002) ─────────────────────
        var dailyLoss = ComputePeriodLoss(equityHistory, 1);
        var weeklyLoss = ComputePeriodLoss(equityHistory, 5);
        var monthlyLoss = ComputePeriodLoss(equityHistory, 21);

        var dailyLimit = dailyLoss.HasValue
            ? new LossLimitStatus { Period = "daily", LimitPct = dailyLimitPct, CurrentLossPct = dailyLoss.Value }
            : null;
        var weeklyLimit = weeklyLoss.HasValue
            ? new LossLimitStatus { Period = "weekly", LimitPct = weeklyLimitPct, CurrentLossPct = weeklyLoss.Value }
            : null;
        var monthlyLimit = monthlyLoss.HasValue
            ? new LossLimitStatus { Period = "monthly", LimitPct = monthlyLimitPct, CurrentLossPct = monthlyLoss.Value }
            : null;

        // ── Step 5: Evaluate graduated thresholds (REQ-DRDN-003) ────────────
        var sizeReductionActive = currentDrawdownPct >= sizeReductionThreshold;
        var addonSuppressed = currentDrawdownPct >= suppressAddonThreshold;
        var reducePositionsAdvisory = currentDrawdownPct >= advisoryReducePositionsThreshold;
        var entrySuppressed = currentDrawdownPct >= suppressEntryThreshold;
        var closeWeakestAdvisory = currentDrawdownPct >= advisoryCloseWeakestThreshold;
        var stopAllTradingAdvisory = currentDrawdownPct >= advisoryStopAllTradingThreshold;

        // ── Step 6: Determine highest threshold level ───────────────────────
        var highestLevel = CalculateHighestThresholdLevel(currentDrawdownPct,
            sizeReductionThreshold, suppressAddonThreshold, advisoryReducePositionsThreshold,
            suppressEntryThreshold, advisoryCloseWeakestThreshold, advisoryStopAllTradingThreshold);

        // ── Step 7: Build advisory message ──────────────────────────────────
        var advisoryMessage = BuildAdvisoryMessage(
            currentDrawdownPct, sizeReductionActive, addonSuppressed,
            reducePositionsAdvisory, entrySuppressed, closeWeakestAdvisory,
            stopAllTradingAdvisory, dailyLimit, weeklyLimit, monthlyLimit);

        // ── Emit metrics ────────────────────────────────────────────────────
        AssessTotal.Add(1, new KeyValuePair<string, object?>("outcome", "success"));

        if (highestLevel > 0)
        {
            ThresholdBreachTotal.Add(1,
                new KeyValuePair<string, object?>("level", highestLevel));
        }

        return new DrawdownAssessment
        {
            CurrentDrawdownPct = currentDrawdownPct,
            HighWaterMark = hwm,
            CurrentEquity = latest.Equity,
            DailyLossLimit = dailyLimit,
            WeeklyLossLimit = weeklyLimit,
            MonthlyLossLimit = monthlyLimit,
            SizeReductionActive = sizeReductionActive,
            AddonSuppressed = addonSuppressed,
            ReducePositionsAdvisory = reducePositionsAdvisory,
            EntrySuppressed = entrySuppressed,
            CloseWeakestAdvisory = closeWeakestAdvisory,
            StopAllTradingAdvisory = stopAllTradingAdvisory,
            HighestThresholdLevel = highestLevel,
            AdvisoryMessage = advisoryMessage,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads equity curve records sorted by session_date ascending.
    /// Returns an empty list when no data is available.
    /// </summary>
    private async Task<List<EquityRecord>> ReadEquityHistoryAsync(string userId, CancellationToken ct)
    {
        var curveCollection = _database.GetCollection<BsonDocument>("equity_curve");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var sort = Builders<BsonDocument>.Sort.Descending("session_date");

        using var cursor = await curveCollection.FindAsync(
            filter,
            new FindOptions<BsonDocument, BsonDocument>
            {
                Sort = sort,
                Limit = MaxEquityRecords,
            },
            ct);

        var docs = await cursor.ToListAsync(ct);

        var records = new List<EquityRecord>(docs.Count);
        foreach (var doc in docs)
        {
            var equityValue = doc.GetValue("equity_value", BsonNull.Value);
            if (!equityValue.IsBsonNull && equityValue.IsDecimal128)
            {
                records.Add(new EquityRecord(
                    (decimal)equityValue.AsDecimal128));
            }
        }

        // Results come back descending by session_date; reverse for
        // chronological (ascending) order used by period-loss computation.
        records.Reverse();

        return records;
    }

    /// <summary>
    /// Reads a <c>decimal</c> value from <c>sys_config</c> by key.
    /// Returns <paramref name="defaultValue"/> when the key is missing,
    /// unparseable, or the read fails.
    /// </summary>
    private async Task<decimal> ReadSysConfigDecimalAsync(string key, decimal defaultValue, CancellationToken ct)
    {
        try
        {
            var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
            var filter = Builders<BsonDocument>.Filter.Eq("key", key);
            var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

            if (doc is null)
                return defaultValue;

            var value = doc.GetValue("value", BsonNull.Value);
            if (!value.IsBsonNull && value.IsString
                && decimal.TryParse(value.AsString, out var parsed))
            {
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DrawdownTracker: failed to read sys_config key '{Key}'. Using default {Default}.",
                key, defaultValue);
        }

        return defaultValue;
    }

    /// <summary>
    /// Computes the percentage loss over a given number of trading sessions
    /// from the equity curve. A positive value indicates a loss; negative
    /// indicates a gain over the period.
    /// Returns null when there are insufficient records.
    /// </summary>
    private static decimal? ComputePeriodLoss(List<EquityRecord> history, int sessionOffset)
    {
        // Need at least sessionOffset + 1 records
        if (history.Count <= sessionOffset)
            return null;

        var current = history[^1].Equity;
        var previous = history[^(1 + sessionOffset)].Equity;

        if (previous <= 0m)
            return null;

        // Positive percentage = loss, negative = gain
        return Math.Round((previous - current) / previous * 100m, 2);
    }

    /// <summary>
    /// Determines the highest threshold level breached.
    /// Levels are evaluated against their actual configured threshold values
    /// (not hardcoded defaults) to support REQ-DRDN-004 configurability.
    /// Returns 0 when no threshold is breached.
    /// </summary>
    private static int CalculateHighestThresholdLevel(
        decimal drawdownPct,
        decimal sizeReductionThreshold,
        decimal suppressAddonThreshold,
        decimal advisoryReducePositionsThreshold,
        decimal suppressEntryThreshold,
        decimal advisoryCloseWeakestThreshold,
        decimal advisoryStopAllTradingThreshold)
    {
        // Evaluate from deepest to shallowest to find the highest breached level.
        if (drawdownPct >= advisoryStopAllTradingThreshold) return 6;
        if (drawdownPct >= advisoryCloseWeakestThreshold) return 5;
        if (drawdownPct >= suppressEntryThreshold) return 4;
        if (drawdownPct >= advisoryReducePositionsThreshold) return 3;
        if (drawdownPct >= suppressAddonThreshold) return 2;
        if (drawdownPct >= sizeReductionThreshold) return 1;

        return 0;
    }

    /// <summary>
    /// Builds a human-readable advisory message summarizing the current
    /// drawdown state, active enforcements, and advisories.
    /// </summary>
    private static string? BuildAdvisoryMessage(
        decimal drawdownPct,
        bool sizeReductionActive,
        bool addonSuppressed,
        bool reducePositionsAdvisory,
        bool entrySuppressed,
        bool closeWeakestAdvisory,
        bool stopAllTradingAdvisory,
        LossLimitStatus? dailyLimit,
        LossLimitStatus? weeklyLimit,
        LossLimitStatus? monthlyLimit)
    {
        var parts = new List<string>();

        if (drawdownPct > 0m)
        {
            parts.Add($"Current drawdown: {drawdownPct:F1}% from peak equity.");
        }
        else
        {
            return null; // No drawdown — no advisory needed
        }

        // Category (a) — platform-enforced
        if (sizeReductionActive)
            parts.Add("Category (a) — Position sizes are being reduced proportionally (5% threshold).");
        if (addonSuppressed)
            parts.Add("Category (a) — Add-on entry recommendations are suppressed (10% threshold).");
        if (entrySuppressed)
            parts.Add("Category (a) — New entry signals are suppressed and the order modal rejects new entry attempts (20% threshold).");

        // Category (b) — user-advisory
        if (reducePositionsAdvisory)
            parts.Add("Category (b) — Advisory: consider reducing open positions (15% threshold).");
        if (closeWeakestAdvisory)
            parts.Add("Category (b) — Advisory: consider closing the weakest positions (25% threshold).");
        if (stopAllTradingAdvisory)
            parts.Add("Category (b) — Advisory: consider stopping all trading activity (30% threshold).");

        // Loss-limit breaches (advisory-only)
        if (dailyLimit?.IsBreached == true)
            parts.Add($"Daily loss limit breached: {dailyLimit.CurrentLossPct:F1}% (limit {dailyLimit.LimitPct}%).");
        if (weeklyLimit?.IsBreached == true)
            parts.Add($"Weekly loss limit breached: {weeklyLimit.CurrentLossPct:F1}% (limit {weeklyLimit.LimitPct}%).");
        if (monthlyLimit?.IsBreached == true)
            parts.Add($"Monthly loss limit breached: {monthlyLimit.CurrentLossPct:F1}% (limit {monthlyLimit.LimitPct}%).");

        return string.Join(" ", parts);
    }

    /// <summary>
    /// Internal carrier for a single equity curve record.
    /// </summary>
    private sealed record EquityRecord(decimal Equity);
}
