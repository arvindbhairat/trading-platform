using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Reads the user's equity base with a snapshot-consistent V1/V2 protocol
/// (REQ-RME-006d), supporting auto-derived equity, manual override with
/// staleness detection (REQ-RME-006c), and the three-figure disclosure
/// panel (REQ-RME-006b).
///
/// <para>Read order:</para>
/// <list type="number">
///   <item>Read <c>ledger_snapshot_version</c> as V1 from the user document.</item>
///   <item>Read equity from <c>equity_curve</c> (latest) or <c>portfolio_snapshots</c>.</item>
///   <item>Read V2 from the user document.</item>
///   <item>If V1 == V2 → consistent. Otherwise retry up to
///       <c>SnapshotRetryMaxAttempts</c> with <c>SnapshotRetryBackoffMs</c> back-off.</item>
///   <item>On exhaustion → return <see cref="EquityBaseSource.SnapshotContentionDeferred"/>.</item>
/// </list>
/// </summary>
internal sealed class EquityBaseReader : IEquityBaseReader
{
    private readonly IMongoDatabase _database;
    private readonly IOptions<EquityReadOptions> _options;
    private readonly ILogger<EquityBaseReader> _logger;

    private static readonly Counter<long> SnapshotRetryTotal = WorkerTelemetry.Meter
        .CreateCounter<long>("rme.equity_read.snapshot_retry_total",
            description: "Equity snapshot-read retries tagged by outcome. " +
                         "REQ-RME-006d.");

    private static readonly Counter<long> SnapshotContentionDeferred = WorkerTelemetry.Meter
        .CreateCounter<long>("rme.equity_read.snapshot_contention_deferred.total",
            description: "Equity reads that exhausted retry and deferred the advisory. " +
                         "REQ-RME-006d.");

    public EquityBaseReader(
        IMongoDatabase database,
        IOptions<EquityReadOptions> options,
        ILogger<EquityBaseReader> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EquityBaseResult> GetEquityBaseAsync(
        string userId,
        CancellationToken ct = default)
    {
        var retryMax = _options.Value.SnapshotRetryMaxAttempts;
        var backoffMs = _options.Value.SnapshotRetryBackoffMs;

        // Pre-read stale days threshold (used by override staleness check)
        var staleDays = await ReadOverrideStaleDaysAsync(ct);

        for (var attempt = 0; attempt <= retryMax; attempt++)
        {
            if (attempt > 0)
            {
                _logger.LogTrace(
                    "EquityBaseReader: snapshot retry attempt {Attempt}/{MaxAttempts} " +
                    "for user {UserId}.", attempt, retryMax, userId);

                await Task.Delay(backoffMs, ct);
            }

            // ── 1) Read V1 ──────────────────────────────────────────────────
            var v1 = await ReadLedgerSnapshotVersionAsync(userId, ct);

            // ── 2) Read equity data ─────────────────────────────────────────
            var equityData = await ReadEquityDataAsync(userId, ct);
            var userDoc = await ReadUserDocumentAsync(userId, ct);

            // ── 3) Read V2 ──────────────────────────────────────────────────
            var v2 = await ReadLedgerSnapshotVersionAsync(userId, ct);

            // ── 4) Check consistency ────────────────────────────────────────
            if (v1 == v2)
            {
                var outcome = attempt == 0 ? "consistent_on_first_read" : "consistent_after_retry";
                SnapshotRetryTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));

                return BuildResult(equityData, userDoc, isConsistent: true, attempt, staleDays);
            }

            _logger.LogTrace(
                "EquityBaseReader: snapshot version changed from V1={V1} to V2={V2} " +
                "for user {UserId}. Retrying.", v1, v2, userId);

            SnapshotRetryTotal.Add(1, new KeyValuePair<string, object?>("outcome", "retry"));
        }

        // ── 5) Retry exhaustion ─────────────────────────────────────────────
        SnapshotRetryTotal.Add(1, new KeyValuePair<string, object?>("outcome", "exhausted"));
        SnapshotContentionDeferred.Add(1);

        _logger.LogWarning(
            "EquityBaseReader: snapshot retry exhausted for user {UserId} " +
            "after {MaxAttempts} attempts. Advisory deferred.",
            userId, retryMax);

        var fallbackEquityData = await ReadEquityDataAsync(userId, ct);
        var fallbackUserDoc = await ReadUserDocumentAsync(userId, ct);

        var fallback = BuildResult(fallbackEquityData, fallbackUserDoc,
            isConsistent: false, retryMax, staleDays);

        return fallback with
        {
            Source = EquityBaseSource.SnapshotContentionDeferred,
            AdvisoryMessage = fallback.IsOverrideActive
                ? "Your equity override is in effect — retrying with current portfolio state."
                : "Portfolio state is updating — please retry shortly.",
        };
    }

    /// <summary>
    /// Reads <c>ledger_snapshot_version</c> from the user's MongoDB document.
    /// </summary>
    private async Task<long> ReadLedgerSnapshotVersionAsync(
        string userId, CancellationToken ct)
    {
        var users = _database.GetCollection<BsonDocument>("users");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var projection = Builders<BsonDocument>.Projection
            .Include("ledger_snapshot_version")
            .Include("_id");

        var doc = await users
            .Find(filter)
            .Project<BsonDocument>(projection)
            .FirstOrDefaultAsync(ct);

        if (doc is null)
        {
            _logger.LogWarning(
                "EquityBaseReader: user {UserId} not found. " +
                "Defaulting ledger_snapshot_version to 0.", userId);
            return 0;
        }

        return doc.GetValue("ledger_snapshot_version", 0L).AsInt64;
    }

    /// <summary>
    /// Reads equity and holdings data from MongoDB.
    /// Uses <c>equity_curve</c> first, then falls back to <c>portfolio_snapshots</c>.
    /// Returns FYERS total, platform-visible equity, and the cash figure.
    /// </summary>
    private async Task<EquityData> ReadEquityDataAsync(
        string userId, CancellationToken ct)
    {
        // ── Try 1: equity_curve (EOD-persisted, most authoritative) ────────
        var equityCurve = _database.GetCollection<BsonDocument>("equity_curve");
        var curveFilter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var sort = Builders<BsonDocument>.Sort.Descending("session_date");

        var latestCurve = await equityCurve
            .Find(curveFilter)
            .Sort(sort)
            .Limit(1)
            .FirstOrDefaultAsync(ct);

        if (latestCurve is not null)
        {
            var equityValue = latestCurve.GetValue("equity_value", BsonNull.Value);
            if (!equityValue.IsBsonNull && equityValue.IsDecimal128)
            {
                var eqVal = (decimal)equityValue.AsDecimal128;
                return new EquityData(
                    FyersTotal: eqVal,
                    PlatformVisible: eqVal,
                    CashReserve: null,
                    Source: "equity_curve");
            }
        }

        // ── Try 2: portfolio_snapshots (cached portfolio state) ────────────
        var snapshots = _database.GetCollection<BsonDocument>("portfolio_snapshots");
        var snapFilter = Builders<BsonDocument>.Filter.Eq("user_id", userId);

        var snapshot = await snapshots
            .Find(snapFilter)
            .FirstOrDefaultAsync(ct);

        if (snapshot is not null)
        {
            var marketValue = snapshot.GetValue("current_market_value", BsonNull.Value);
            var cashReserve = snapshot.GetValue("cash_reserve", BsonNull.Value);

            var mv = marketValue.IsBsonNull || !marketValue.IsDecimal128
                ? 0m
                : (decimal)marketValue.AsDecimal128;

            var cash = cashReserve.IsBsonNull || !cashReserve.IsDecimal128
                ? 0m
                : (decimal)cashReserve.AsDecimal128;

            var platformVisible = mv + cash;

            return new EquityData(
                FyersTotal: platformVisible,
                PlatformVisible: platformVisible,
                CashReserve: cash,
                Source: "portfolio_snapshots");
        }

        // ── No data available — return zero ─────────────────────────────────
        return new EquityData(
            FyersTotal: null,
            PlatformVisible: null,
            CashReserve: null,
            Source: "none");
    }

    /// <summary>
    /// Reads the user's MongoDB document for override and metadata.
    /// </summary>
    private async Task<BsonDocument?> ReadUserDocumentAsync(
        string userId, CancellationToken ct)
    {
        var users = _database.GetCollection<BsonDocument>("users");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);

        return await users.Find(filter).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Builds the final <see cref="EquityBaseResult"/> from equity data and
    /// user document, applying override logic (REQ-RME-006c).
    /// </summary>
    private EquityBaseResult BuildResult(
        EquityData equityData,
        BsonDocument? userDoc,
        bool isConsistent,
        int retryAttempts,
        int staleDays = 30)
    {
        // ── Determine source figures ────────────────────────────────────────
        var fyersTotal = equityData.FyersTotal;
        var platformVisible = equityData.PlatformVisible;

        // External holdings = FYERS total - platform visible
        decimal? externalExcluded = null;
        if (fyersTotal.HasValue && platformVisible.HasValue)
        {
            externalExcluded = fyersTotal.Value - platformVisible.Value;
        }

        // ── Check manual override (REQ-RME-006c) ────────────────────────────
        bool isOverrideActive = false;
        bool isOverrideStale = false;
        decimal? overrideValue = null;
        DateTime? overrideUpdatedAt = null;
        string? advisoryMessage = null;

        if (userDoc is not null)
        {
            var overrideField = userDoc.GetValue("equity_base_override", BsonNull.Value);
            if (!overrideField.IsBsonNull && overrideField.IsDecimal128)
            {
                overrideValue = (decimal)overrideField.AsDecimal128;
            }

            var updatedAtField = userDoc.GetValue("equity_base_override_updated_at", BsonNull.Value);
            if (!updatedAtField.IsBsonNull && updatedAtField.IsBsonDateTime)
            {
                overrideUpdatedAt = updatedAtField.ToUniversalTime();
            }
        }

        if (overrideValue.HasValue && overrideValue.Value > 0)
        {
            isOverrideActive = true;

            // Check staleness against the configured threshold
            if (overrideUpdatedAt.HasValue)
            {
                isOverrideStale = (DateTime.UtcNow - overrideUpdatedAt.Value).TotalDays > staleDays;

                if (isOverrideStale)
                {
                    advisoryMessage =
                        $"Your equity override of ₹{overrideValue.Value:N0} " +
                        $"was set on {overrideUpdatedAt.Value:yyyy-MM-dd} " +
                        $"and has not been reviewed in over {staleDays} days. " +
                        "Please review, update, or clear it in your profile settings.";
                }
            }
        }

        // ── Resolve equity base ─────────────────────────────────────────────
        decimal equityBase;
        EquityBaseSource source;

        if (isOverrideActive)
        {
            equityBase = overrideValue!.Value;
            source = EquityBaseSource.ManualOverride;
        }
        else if (platformVisible.HasValue && platformVisible.Value > 0)
        {
            equityBase = platformVisible.Value;
            source = EquityBaseSource.AutoDerived;
        }
        else
        {
            equityBase = 0m;
            source = EquityBaseSource.Zero;
            advisoryMessage ??=
                "Your account balance could not be determined. " +
                "All position sizing recommendations are blocked " +
                "until a valid equity value is available.";
        }

        return new EquityBaseResult
        {
            EquityBase = equityBase,
            Source = source,
            IsSnapshotConsistent = isConsistent,
            SnapshotRetryAttempts = retryAttempts,
            FyersTotalAccountValue = fyersTotal,
            PlatformVisibleEquity = platformVisible,
            ExternalHoldingsExcluded = externalExcluded,
            IsOverrideActive = isOverrideActive,
            IsOverrideStale = isOverrideStale,
            AdvisoryMessage = advisoryMessage,
        };
    }

    /// <summary>
    /// Reads the stale override threshold from sys_config.
    /// Cached per-call — the underlying MongoDB value is read on each
    /// invocation, which is acceptable since this method is called at most
    /// once per equity read.
    /// </summary>
    private async Task<int> ReadOverrideStaleDaysAsync(CancellationToken ct)
    {
        try
        {
            var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
            var filter = Builders<BsonDocument>.Filter.Eq("key", "risk.equity_override_stale_days");
            var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

            if (doc is null)
                return 30;

            var value = doc.GetValue("value", BsonNull.Value);
            if (!value.IsBsonNull && int.TryParse(value.AsString, out var parsed))
                return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "EquityBaseReader: failed to read risk.equity_override_stale_days " +
                "from sys_config. Using default 30.");
        }

        return 30;
    }

    /// <summary>
    /// Internal carrier for equity and holdings data read from MongoDB.
    /// </summary>
    private sealed record EquityData(
        decimal? FyersTotal,
        decimal? PlatformVisible,
        decimal? CashReserve,
        string Source);
}
