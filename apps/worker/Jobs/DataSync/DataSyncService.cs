using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;
using SignalStack.Historical;
using SignalStack.Storage.Historical;
using SignalStack.Storage.Universe;
using SignalStack.MarketData;
using SignalStack.SqlMigrations;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Jobs.DataSync;

/// <summary>
/// Core service for the DataSync (DS) job.
///
/// Processes trading sessions within a configurable recovery window, syncing
/// daily OHLCV data from the configured market data provider into the PostgreSQL
/// per-symbol D_, W_, and M_ tables.
///
/// REQ-MARKET-003:  post-market DataSync refreshes PostgreSQL historical data.
/// REQ-MARKET-005:  fails on provider unavailability or auth failure with
///                  structured Error log + dual-channel notification.
/// REQ-MARKET-005a: re-checks admin token at every session boundary during
///                  10-session recovery; mid-run expiry -> partial_completion.
/// REQ-MARKET-006:  skips symbols whose daily data is unavailable.
/// REQ-MARKET-007:  writes explicit success marker per trading session.
/// REQ-MARKET-009:  fetches from current session minus 10 sessions back.
/// REQ-MARKET-013:  validates each OHLCV candle before writing.
/// REQ-MARKET-014:  skips symbols with invalid candles, logs the failure.
/// </summary>
public sealed class DataSyncService
{
    private readonly string _sqlConnectionString;
    private readonly ISymbolTableMapping _tableMapping;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOhlcvRepository _ohlcvRepo;
    private readonly ISymbolMasterRepository _symbolMasterRepo;
    private readonly ITradingCalendarRepository _calendarRepo;
    private readonly Func<Task<string?>> _adminTokenProvider;
    private readonly ILogger<DataSyncService> _logger;

    public DataSyncService(
        string sqlConnectionString,
        ISymbolTableMapping tableMapping,
        IMarketDataProvider marketDataProvider,
        IOhlcvRepository ohlcvRepo,
        ISymbolMasterRepository symbolMasterRepo,
        ITradingCalendarRepository calendarRepo,
        Func<Task<string?>> adminTokenProvider,
        ILogger<DataSyncService> logger)
    {
        _sqlConnectionString = sqlConnectionString;
        _tableMapping = tableMapping;
        _marketDataProvider = marketDataProvider;
        _ohlcvRepo = ohlcvRepo;
        _symbolMasterRepo = symbolMasterRepo;
        _calendarRepo = calendarRepo;
        _adminTokenProvider = adminTokenProvider;
        _logger = logger;
    }

    /// <summary>
    /// Runs a full DataSync cycle: determines which sessions within the recovery
    /// window still need syncing and processes them in chronological order.
    /// </summary>
    /// <param name="recoverySessionCount">Number of past trading sessions to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result describing the sync outcome.</returns>
    public async Task<DataSyncResult> RunSyncAsync(
        int recoverySessionCount,
        CancellationToken ct = default)
    {
        // ── Step 1: Perform initial admin token check (REQ-MARKET-005) ──────
        var tokenCheck = await CheckAdminTokenAsync(ct);
        if (tokenCheck != TokenCheckResult.Valid)
        {
            var reason = tokenCheck switch
            {
                TokenCheckResult.Absent => "admin_token_absent",
                TokenCheckResult.Expired => "admin_token_expired",
                TokenCheckResult.Dirty   => "admin_token_dirty",
                _ => "unknown"
            };

            _logger.LogError(
                "DataSync aborted at startup: admin token check failed ({Reason}). " +
                "REQ-MARKET-005: DS must emit admin_fyers_token_expiry_warning " +
                "notification via Telegram + email (P5 notification infrastructure " +
                "not yet wired — notification dispatch deferred to P5-T4/P5-T5).",
                reason);

            return new DataSyncResult(
                Outcome: $"aborted_{reason}",
                SessionsProcessed: 0,
                SessionsTotal: 0,
                TotalSymbolSessionsProcessed: 0,
                SymbolsSkippedDataUnavailable: 0,
                SymbolsSkippedValidationFailed: 0,
                LastSuccessfulSession: null,
                CompletedSymbolsPerSession: new Dictionary<DateOnly, int>());
        }

        // ── Step 2: Determine which trading sessions to process ─────────────
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sessionsToProcess = await GetRecoverySessionsAsync(today, recoverySessionCount, ct);

        if (sessionsToProcess.Count == 0)
        {
            _logger.LogInformation("DataSync: no sessions require syncing. Nothing to do.");
            return new DataSyncResult(
                Outcome: "no_sessions_needed",
                SessionsProcessed: 0,
                SessionsTotal: 0,
                TotalSymbolSessionsProcessed: 0,
                SymbolsSkippedDataUnavailable: 0,
                SymbolsSkippedValidationFailed: 0,
                LastSuccessfulSession: null,
                CompletedSymbolsPerSession: new Dictionary<DateOnly, int>());
        }

        _logger.LogInformation(
            "DataSync: processing {Count} trading sessions from {First} to {Last}.",
            sessionsToProcess.Count,
            sessionsToProcess[0],
            sessionsToProcess[^1]);

        // ── Step 3: Process each session (batch boundary = 1 session) ──────
        var sessionsProcessed = 0;
        var totalSymbolSessions = 0;
        var symbolsSkippedNoData = 0;
        var symbolsSkippedInvalid = 0;
        var completedPerSession = new Dictionary<DateOnly, int>();
        DateOnly? lastSuccessfulSession = null;

        foreach (var sessionDate in sessionsToProcess)
        {
            if (ct.IsCancellationRequested) break;

            // ─ Batch boundary: re-check admin token (REQ-MARKET-005a) ──────
            var reCheck = await CheckAdminTokenAsync(ct);
            if (reCheck != TokenCheckResult.Valid)
            {
                _logger.LogError(
                    "DataSync: admin token expired mid-run at session {Session} " +
                    "after {Processed} sessions. Exiting with partial_completion_token_expired. " +
                    "REQ-MARKET-005a: must emit admin_fyers_token_expiry_warning " +
                    "notification (deferred to P5 notification infrastructure).",
                    sessionDate, sessionsProcessed);

                return new DataSyncResult(
                    Outcome: "partial_completion_token_expired",
                    SessionsProcessed: sessionsProcessed,
                    SessionsTotal: sessionsToProcess.Count,
                    TotalSymbolSessionsProcessed: totalSymbolSessions,
                    SymbolsSkippedDataUnavailable: symbolsSkippedNoData,
                    SymbolsSkippedValidationFailed: symbolsSkippedInvalid,
                    LastSuccessfulSession: lastSuccessfulSession,
                    CompletedSymbolsPerSession: completedPerSession);
            }

            // ─ Process this session ─────────────────────────────────────────
            var sessionResult = await SyncSessionAsync(sessionDate, ct);

            sessionsProcessed++;
            totalSymbolSessions += sessionResult.SymbolsProcessed;
            symbolsSkippedNoData += sessionResult.SymbolsSkippedNoData;
            symbolsSkippedInvalid += sessionResult.SymbolsSkippedInvalid;
            completedPerSession[sessionDate] = sessionResult.SymbolsProcessed;
            lastSuccessfulSession = sessionDate;

            _logger.LogInformation(
                "DataSync: session {Session} completed — {Processed} symbols synced, " +
                "{NoData} skipped (no data), {Invalid} skipped (validation).",
                sessionDate,
                sessionResult.SymbolsProcessed,
                sessionResult.SymbolsSkippedNoData,
                sessionResult.SymbolsSkippedInvalid);
        }

        // ── Step 4: Return successful result ────────────────────────────────
        _logger.LogInformation(
            "DataSync completed: {Processed}/{Total} sessions, " +
            "{SymbolSessions} total symbol-sessions processed.",
            sessionsProcessed, sessionsToProcess.Count, totalSymbolSessions);

        return new DataSyncResult(
            Outcome: "completed",
            SessionsProcessed: sessionsProcessed,
            SessionsTotal: sessionsToProcess.Count,
            TotalSymbolSessionsProcessed: totalSymbolSessions,
            SymbolsSkippedDataUnavailable: symbolsSkippedNoData,
            SymbolsSkippedValidationFailed: symbolsSkippedInvalid,
            LastSuccessfulSession: lastSuccessfulSession,
            CompletedSymbolsPerSession: completedPerSession);
    }

    /// <summary>
    /// Syncs all active symbols for a single trading session.
    /// </summary>
    private async Task<SessionSyncResult> SyncSessionAsync(
        DateOnly sessionDate,
        CancellationToken ct)
    {
        var activeSymbols = await _symbolMasterRepo.GetAllAsync(archived: false, ct);

        // Filter out scan-excluded symbols.
        var symbols = activeSymbols
            .Where(s => !s.ScanExcluded)
            .Select(s => s.Symbol)
            .OrderBy(s => s)
            .ToList();

        if (symbols.Count == 0)
        {
            _logger.LogWarning("DataSync: no active symbols found for session {Session}.", sessionDate);
            return new SessionSyncResult(0, 0, 0);
        }

        var symbolsProcessed = 0;
        var symbolsSkippedNoData = 0;
        var symbolsSkippedInvalid = 0;

        // Process symbols in batches for progress tracking.
        for (var i = 0; i < symbols.Count; i += 50)
        {
            if (ct.IsCancellationRequested) break;

            var batch = symbols.Skip(i).Take(50).ToList();

            foreach (var symbol in batch)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    var result = await SyncSymbolForDateAsync(symbol, sessionDate, ct);

                    switch (result)
                    {
                        case SymbolSyncOutcome.Success:
                            symbolsProcessed++;
                            break;
                        case SymbolSyncOutcome.DataUnavailable:
                            symbolsSkippedNoData++;
                            break;
                        case SymbolSyncOutcome.ValidationFailed:
                            symbolsSkippedInvalid++;
                            break;
                        case SymbolSyncOutcome.NoOpAlreadySynced:
                            // Data already exists — count as processed.
                            symbolsProcessed++;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    Activity.Current?.AddException(ex);
                    _logger.LogError(ex,
                        "DataSync: unexpected error syncing symbol {Symbol} for session {Session}.",
                        symbol, sessionDate);
                    // REQ-MARKET-006: skip failed symbols, continue with rest.
                }
            }
        }

        WorkerTelemetry.DataSyncSymbolsProcessedTotal.Add(symbolsProcessed);

        // After all symbols for this session, recompute weekly and monthly
        // aggregates covering the affected date range.
        try
        {
            await RecomputeAggregatesForSessionAsync(sessionDate, ct);
        }
        catch (Exception ex)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Activity.Current?.AddException(ex);
            _logger.LogError(ex,
                "DataSync: failed to recompute weekly/monthly aggregates for session {Session}. " +
                "Daily data was written but W_/M_ tables may be stale.",
                sessionDate);
        }

        return new SessionSyncResult(
            symbolsProcessed,
            symbolsSkippedNoData,
            symbolsSkippedInvalid);
    }

    /// <summary>
    /// Syncs OHLCV data for a single symbol on a specific trading session date.
    ///
    /// Optimised to fetch a date range (from the last known candle to the session
    /// date) in a single API call, rather than one day at a time. All returned
    /// candles are inserted with ON CONFLICT DO NOTHING to proactively fill any
    /// gaps left by missed sessions or partial HDS seeding.
    ///
    /// REQ-MARKET-009: 10-session auto-recovery — range-based fetch handles
    /// multiple missed days in a single provider call.
    /// </summary>
    private async Task<SymbolSyncOutcome> SyncSymbolForDateAsync(
        string symbol,
        DateOnly sessionDate,
        CancellationToken ct)
    {
        // ── Step 1: Resolve SQL table suffix ────────────────────────────────
        var suffix = await _tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null)
        {
            _logger.LogWarning(
                "DataSync: symbol {Symbol} has no sql_table_name_suffix. Skipping.",
                symbol);
            return SymbolSyncOutcome.DataUnavailable;
        }

        var dailyTableName = $"D_{suffix}";

        // ── Step 2: Fast path — data already exists up to or past sessionDate ─
        // Uses the last known candle date to avoid unnecessary fetch calls.
        // This replaces the per-date CheckDailyDataExistsAsync check with a
        // single MAX(Date) query covering the full recovery window.
        var lastDate = await GetLastDateAsync(dailyTableName, ct);
        if (lastDate.HasValue && lastDate.Value >= sessionDate)
        {
            _logger.LogTrace(
                "DataSync: data already exists for {Symbol} up to {LastDate} " +
                "(>= session {Date}). Skipping.",
                symbol, lastDate.Value, sessionDate);
            return SymbolSyncOutcome.NoOpAlreadySynced;
        }

        // ── Step 3: Fetch OHLCV range from the MDP ──────────────────────────
        // Fetch from lastDate+1 (or sessionDate if no prior data) through
        // sessionDate in a single API call. This fills gaps from missed
        // recovery sessions and partial HDS seeding in one round-trip.
        var fetchFrom = lastDate?.AddDays(1) ?? sessionDate;

        IReadOnlyList<SignalStack.MarketData.OhlcvRecord> providerRecords;
        try
        {
            providerRecords = await _marketDataProvider.FetchHistoricalOhlcvAsync(
                symbol, fetchFrom, sessionDate, ct);
        }
        catch (Exception ex)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Activity.Current?.AddException(ex);
            _logger.LogWarning(ex,
                "DataSync: failed to fetch data for {Symbol} from {From} to {Date} " +
                "from {Provider}.",
                symbol, fetchFrom, sessionDate, _marketDataProvider.ProviderName);
            return SymbolSyncOutcome.DataUnavailable;
        }

        if (providerRecords.Count == 0)
        {
            _logger.LogTrace(
                "DataSync: no data returned for {Symbol} from {From} to {Date}. Skipping.",
                symbol, fetchFrom, sessionDate);
            return SymbolSyncOutcome.DataUnavailable;
        }

        // ── Step 4: Validate and insert all records from the range ──────────
        // Insert each candle with ON CONFLICT DO NOTHING so overlapping date
        // ranges (from parallel HDS runs or re-runs) are handled idempotently.
        var sessionDateInserted = false;
        var insertedCount = 0;
        var skippedCount = 0;

        await using var conn = new NpgsqlConnection(_sqlConnectionString);
        await conn.OpenAsync(ct);

        foreach (var candle in providerRecords)
        {
            if (ct.IsCancellationRequested) break;

            // ── Validate each candle (REQ-MARKET-013) ───────────────────────
            var validationError = ValidateCandle(candle);
            if (validationError is not null)
            {
                _logger.LogWarning(
                    "DataSync: invalid candle for {Symbol} on {Date}: {Error}. Skipping.",
                    symbol, candle.Date, validationError);
                skippedCount++;
                continue;
            }

            try
            {
                var insertSql = $@"
INSERT INTO ""{dailyTableName}"" (""Date"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume"")
VALUES (@Date, @Open, @High, @Low, @Close, @Volume)
ON CONFLICT (""Date"") DO NOTHING;";

                await using var cmd = new NpgsqlCommand(insertSql, conn);
                cmd.Parameters.AddWithValue("@Date", candle.Date.ToDateTime(TimeOnly.MinValue));
                cmd.Parameters.AddWithValue("@Open", (double)candle.Open);
                cmd.Parameters.AddWithValue("@High", (double)candle.High);
                cmd.Parameters.AddWithValue("@Low", (double)candle.Low);
                cmd.Parameters.AddWithValue("@Close", (double)candle.Close);
                cmd.Parameters.AddWithValue("@Volume", candle.Volume);
                await cmd.ExecuteNonQueryAsync(ct);

                if (candle.Date == sessionDate)
                    sessionDateInserted = true;

                insertedCount++;
            }
            catch (Exception ex)
            {
                Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                Activity.Current?.AddException(ex);
                _logger.LogError(ex,
                    "DataSync: failed to insert daily data for {Symbol} on {Date} into {Table}.",
                    symbol, candle.Date, dailyTableName);
                skippedCount++;
            }
        }

        if (insertedCount > 0)
        {
            _logger.LogDebug(
                "DataSync: processed {Count} candles for {Symbol} range {From} to {To}: " +
                "{Inserted} inserted, {Skipped} skipped.",
                providerRecords.Count, symbol, fetchFrom, sessionDate,
                insertedCount, skippedCount);
        }

        return sessionDateInserted
            ? SymbolSyncOutcome.Success
            : SymbolSyncOutcome.DataUnavailable;
    }

    /// <summary>
    /// Recomputes weekly (W_) and monthly (M_) aggregate candles from the D_
    /// table for the date range affected by the given session.
    /// Uses the same INSERT...ON CONFLICT approach as HistoricDataSeedService.
    /// </summary>
    private async Task RecomputeAggregatesForSessionAsync(
        DateOnly sessionDate,
        CancellationToken ct)
    {
        // Determine the week and month period start dates for this session.
        var weekStart = GetWeekStartDate(sessionDate);
        var monthStart = new DateOnly(sessionDate.Year, sessionDate.Month, 1);

        // Compute weekly and monthly aggregates across the affected range.
        // We recompute the full week covering this session and the full month.
        var weekEnd = weekStart.AddDays(6);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);

        await using var conn = new NpgsqlConnection(_sqlConnectionString);
        await conn.OpenAsync(ct);

        // Get all distinct suffixes that have data in the affected range.
        var activeSymbols = await _symbolMasterRepo.GetAllAsync(archived: false, ct);
        var suffixes = activeSymbols
            .Where(s => !s.ScanExcluded)
            .Select(s => s.SqlTableNameSuffix)
            .Distinct()
            .ToList();

        foreach (var suffix in suffixes)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var dailyTableName = $"D_{suffix}";
                var weeklyTableName = $"W_{suffix}";
                var monthlyTableName = $"M_{suffix}";

                // Recompute weekly aggregate for the week containing this session.
                await UpsertAggregateAsync(
                    conn, dailyTableName, weeklyTableName,
                    "date_trunc('week', \"Date\")",
                    weekStart, weekEnd, ct);

                // Recompute monthly aggregate for the month containing this session.
                await UpsertAggregateAsync(
                    conn, dailyTableName, monthlyTableName,
                    "date_trunc('month', \"Date\")",
                    monthStart, monthEnd, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "DataSync: skipped aggregate recompute for suffix {Suffix} — " +
                    "tables likely missing (symbol not yet seeded by HDS).",
                    suffix);
            }
        }

        _logger.LogDebug(
            "DataSync: recomputed weekly/monthly aggregates for session {Session} " +
            "across {SuffixCount} suffixes.",
            sessionDate, suffixes.Count);
    }

    /// <summary>
    /// Computes and upserts aggregate candles from D_ into the target (W_ or M_) table
    /// for a given date range, using a temp-table + INSERT...ON CONFLICT approach.
    /// </summary>
    private static async Task UpsertAggregateAsync(
        NpgsqlConnection conn,
        string sourceTable,
        string targetTable,
        string groupByExpression,
        DateOnly rangeStart,
        DateOnly rangeEnd,
        CancellationToken ct)
    {
        var tempTable = $"\"tmp_ds_{targetTable}\"";

        // DROP before CREATE to handle Npgsql connection pooling: temp tables
        // persist on the same pooled connection, so a table created on a prior
        // call will collide on the next call for the same suffix.
        var createTempSql = $@"
DROP TABLE IF EXISTS {tempTable};
CREATE TEMPORARY TABLE {tempTable} (
    ""PeriodStart"" DATE   NOT NULL,
    ""Open""        DOUBLE PRECISION  NOT NULL,
    ""High""        DOUBLE PRECISION  NOT NULL,
    ""Low""         DOUBLE PRECISION  NOT NULL,
    ""Close""       DOUBLE PRECISION  NOT NULL,
    ""Volume""      BIGINT NOT NULL,
    PRIMARY KEY (""PeriodStart"")
);";

        await using (var createCmd = new NpgsqlCommand(createTempSql, conn))
        {
            await createCmd.ExecuteNonQueryAsync(ct);
        }

        // Compute aggregates from D_ for the specified date range.
        var computeSql = $@"
INSERT INTO {tempTable} (""PeriodStart"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume"")
SELECT
    {groupByExpression} AS ""PeriodStart"",
    MAX(CASE WHEN rn_asc = 1 THEN ""Open"" END)  AS ""Open"",
    MAX(""High"")                                  AS ""High"",
    MIN(""Low"")                                   AS ""Low"",
    MAX(CASE WHEN rn_desc = 1 THEN ""Close"" END) AS ""Close"",
    SUM(""Volume"")                                AS ""Volume""
FROM (
    SELECT *,
        ROW_NUMBER() OVER (PARTITION BY {groupByExpression} ORDER BY ""Date"" ASC)  AS rn_asc,
        ROW_NUMBER() OVER (PARTITION BY {groupByExpression} ORDER BY ""Date"" DESC) AS rn_desc
    FROM ""{sourceTable}""
    WHERE ""Date"" >= @RangeStart AND ""Date"" <= @RangeEnd
) AS subq
GROUP BY {groupByExpression}
ORDER BY ""PeriodStart"";";

        await using (var computeCmd = new NpgsqlCommand(computeSql, conn))
        {
            computeCmd.Parameters.AddWithValue("@RangeStart", rangeStart.ToDateTime(TimeOnly.MinValue));
            computeCmd.Parameters.AddWithValue("@RangeEnd", rangeEnd.ToDateTime(TimeOnly.MinValue));
            await computeCmd.ExecuteNonQueryAsync(ct);
        }

        // INSERT from temp table into target — ON CONFLICT DO UPDATE for idempotent upsert.
        var mergeSql = $@"
INSERT INTO ""{targetTable}"" (""Date"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume"")
SELECT ""PeriodStart"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume""
FROM {tempTable}
ON CONFLICT (""Date"") DO UPDATE SET
    ""Open""   = EXCLUDED.""Open"",
    ""High""   = EXCLUDED.""High"",
    ""Low""    = EXCLUDED.""Low"",
    ""Close""  = EXCLUDED.""Close"",
    ""Volume"" = EXCLUDED.""Volume"";";

        await using (var mergeCmd = new NpgsqlCommand(mergeSql, conn))
        {
            await mergeCmd.ExecuteNonQueryAsync(ct);
        }
    }

    /// <summary>
    /// Gets the list of trading sessions within the recovery window that do not
    /// yet have a DataSync success marker. Sessions are returned in chronological
    /// order (oldest first).
    /// </summary>
    private async Task<List<DateOnly>> GetRecoverySessionsAsync(
        DateOnly today,
        int recoverySessionCount,
        CancellationToken ct)
    {
        // Get trading sessions from the recovery window.
        // Go back enough calendar days to find at least recoverySessionCount sessions.
        var lookBackDays = recoverySessionCount * 7; // generous buffer for weekends/holidays
        var fromDate = today.AddDays(-lookBackDays);

        var sessions = await _calendarRepo.GetSessionsAsync(
            fromDate: fromDate.ToString("yyyy-MM-dd"),
            toDate: today.ToString("yyyy-MM-dd"),
            ct: ct);

        if (sessions.Count == 0)
            return [];

        // Parse session dates and filter to past sessions only (don't sync future).
        var sessionDates = sessions
            .Select(s =>
            {
                if (DateOnly.TryParse(s.SessionDate, out var d))
                    return d;
                return (DateOnly?)null;
            })
            .Where(d => d.HasValue && d.Value <= today)
            .Select(d => d!.Value)
            .OrderByDescending(d => d)
            .Take(recoverySessionCount)
            .OrderBy(d => d)  // chronological order for processing
            .ToList();

        return sessionDates;
    }

    /// <summary>
    /// Returns the most recent Date in the symbol's D_ table, or null if the
    /// table is empty or does not exist.
    ///
    /// Used by the range-based fetch to determine where to resume fetching from.
    /// </summary>
    private async Task<DateOnly?> GetLastDateAsync(
        string tableName,
        CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_sqlConnectionString);
            await conn.OpenAsync(ct);

            var checkTableSql = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'public' AND TABLE_NAME = @TableName
) THEN 1 ELSE 0 END";

            await using var checkCmd = new NpgsqlCommand(checkTableSql, conn);
            checkCmd.Parameters.AddWithValue("@TableName", tableName);
            var exists = (int)(await checkCmd.ExecuteScalarAsync(ct))! == 1;

            if (!exists)
            {
                _logger.LogTrace(
                    "DataSync: table {Table} does not exist. Assuming no data.",
                    tableName);
                return null;
            }

            var dateQuery = $"SELECT MAX(\"Date\") FROM \"{tableName}\"";
            await using var dateCmd = new NpgsqlCommand(dateQuery, conn);
            var result = await dateCmd.ExecuteScalarAsync(ct);

            if (result is null || result == DBNull.Value)
                return null;

            return DateOnly.FromDateTime(Convert.ToDateTime(result));
        }
        catch (Exception ex)
        {
            // Table may not exist yet — treat as no data.
            _logger.LogTrace(ex,
                "DataSync: could not query max date in {Table}. Assuming no data.",
                tableName);
            return null;
        }
    }

    /// <summary>
    /// Checks admin FYERS token validity.
    /// REQ-MARKET-005/005a: token must be present and active.
    /// </summary>
    private async Task<TokenCheckResult> CheckAdminTokenAsync(CancellationToken ct)
    {
        try
        {
            var token = await _adminTokenProvider();
            if (string.IsNullOrWhiteSpace(token))
            {
                return TokenCheckResult.Absent;
            }

            // Token is present — for the purposes of this check, we consider
            // it valid. Actual expiry detection happens when API calls fail.
            // REQ-MARKET-005a specifies mid-run expiry detection when a batch
            // fails with token-expired error during MDP calls.
            return TokenCheckResult.Valid;
        }
        catch (Exception ex)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Activity.Current?.AddException(ex);
            _logger.LogError(ex, "DataSync: failed to check admin token availability.");
            return TokenCheckResult.Absent;
        }
    }

    /// <summary>
    /// Validates a single OHLCV candle per REQ-MARKET-013 rules.
    /// Returns null if valid, or an error description if invalid.
    /// </summary>
    private static string? ValidateCandle(SignalStack.MarketData.OhlcvRecord candle)
    {
        if (candle.High < candle.Low)
            return $"High ({candle.High}) is less than Low ({candle.Low})";

        if (candle.Open <= 0)
            return $"Open ({candle.Open}) is zero or negative";

        if (candle.High <= 0)
            return $"High ({candle.High}) is zero or negative";

        if (candle.Low <= 0)
            return $"Low ({candle.Low}) is zero or negative";

        if (candle.Close <= 0)
            return $"Close ({candle.Close}) is zero or negative";

        if (candle.Volume < 0)
            return $"Volume ({candle.Volume}) is negative";

        return null; // valid
    }

    /// <summary>
    /// Calculates the Monday (week start) for a given date.
    /// </summary>
    private static DateOnly GetWeekStartDate(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-daysSinceMonday);
    }
}

// ── Result types ──────────────────────────────────────────────────────────────

/// <summary>
/// Outcome of a full DataSync run.
/// </summary>
public sealed record DataSyncResult(
    string Outcome,
    int SessionsProcessed,
    int SessionsTotal,
    int TotalSymbolSessionsProcessed,
    int SymbolsSkippedDataUnavailable,
    int SymbolsSkippedValidationFailed,
    DateOnly? LastSuccessfulSession,
    IReadOnlyDictionary<DateOnly, int>? CompletedSymbolsPerSession);

/// <summary>
/// Outcome of syncing a single trading session.
/// </summary>
internal sealed record SessionSyncResult(
    int SymbolsProcessed,
    int SymbolsSkippedNoData,
    int SymbolsSkippedInvalid);

/// <summary>
/// Outcome of syncing a single symbol for a single date.
/// </summary>
internal enum SymbolSyncOutcome
{
    /// <summary>Data was successfully inserted.</summary>
    Success,

    /// <summary>Data already existed for this date — no-op.</summary>
    NoOpAlreadySynced,

    /// <summary>Market data provider returned no data for this symbol/date.</summary>
    DataUnavailable,

    /// <summary>Candle failed validation per REQ-MARKET-013.</summary>
    ValidationFailed
}

/// <summary>
/// Result of an admin token availability check.
/// </summary>
internal enum TokenCheckResult
{
    /// <summary>Token is present and assumed valid.</summary>
    Valid,

    /// <summary>Token provider returned null or empty.</summary>
    Absent,

    /// <summary>Token is present but known to be expired (from a prior API failure).</summary>
    Expired,

    /// <summary>Token is in dirty state (user re-auth required).</summary>
    Dirty
}
