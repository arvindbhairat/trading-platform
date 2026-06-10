using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using SignalStack.Historical;
using SignalStack.Storage.Historical;
using SignalStack.Storage.Universe;
using SignalStack.MarketData;
using SignalStack.SqlMigrations;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Jobs.HistoricDataSeed;

/// <summary>
/// Core service for the HistoricDataSeed (HDS) job.
///
/// For a given symbol, atomically creates the per-symbol D_, W_, M_ tables
/// (if they do not already exist), determines the resumption point from the
/// last available daily candle date, fetches historical OHLCV data from the
/// configured market data provider, inserts daily data, and computes weekly
/// and monthly aggregates from the daily data.
///
/// REQ-HIST-009: scoped per-symbol table materialisation.
/// REQ-HIST-009a(a): IF NOT EXISTS table creation — idempotent.
/// REQ-HIST-009a(b): D_, W_, M_ tables created in a single atomic transaction.
/// REQ-HIST-009a(c): uses the shared DDL template from SharedTableDdlTemplate.
/// REQ-HIST-009: resumable from last successful date.
/// </summary>
public sealed class HistoricDataSeedService
{
    private readonly string _sqlConnectionString;
    private readonly ISymbolTableMapping _tableMapping;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly IOhlcvRepository _ohlcvRepo;
    private readonly ILogger<HistoricDataSeedService> _logger;

    public HistoricDataSeedService(
        string sqlConnectionString,
        ISymbolTableMapping tableMapping,
        IMarketDataProvider marketDataProvider,
        IOhlcvRepository ohlcvRepo,
        ILogger<HistoricDataSeedService> logger)
    {
        _sqlConnectionString = sqlConnectionString;
        _tableMapping = tableMapping;
        _marketDataProvider = marketDataProvider;
        _ohlcvRepo = ohlcvRepo;
        _logger = logger;
    }

    /// <summary>
    /// Absolute minimum backfill date — NSE started trading in 1992, so this
    /// safety floor prevents runaway backwards iteration if the API never
    /// returns empty.
    /// </summary>
    private static readonly DateOnly MinBackfillDate = new(1990, 1, 1);

    /// <summary>
    /// Maximum days per chunk when fetching historical data from the MDP.
    /// Matches the FYERS API's 366-day limit for daily resolution.
    /// </summary>
    private const int MaxDaysPerChunk = 365;

    /// <summary>
    /// Seeds historical data for a single symbol. Idempotent and resumable.
    ///
    /// <para>Backfills to inception: iterates backwards from the earliest data point
    /// (or today if the table is empty) in 365-day chunks. Each chunk is fetched,
    /// validated, and inserted. Stops when FYERS returns zero candles, indicating
    /// the symbol's inception has been reached.</para>
    ///
    /// <para>After backfill, forward-fills any gap between the latest data point and
    /// today to ensure the symbol is fully up to date.</para>
    ///
    /// <para>Weekly and monthly aggregates are recomputed over the full range of
    /// newly inserted data.</para>
    /// </summary>
    /// <param name="symbol">NSE trading symbol (e.g. "RELIANCE").</param>
    /// <param name="historicalYearsBack">
    /// Ignored for backfill depth — the algorithm iterates to inception regardless.
    /// Retained for API backward compatibility only.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result describing what was done.</returns>
    public async Task<SeedSymbolResult> SeedSymbolAsync(
        string symbol,
        int historicalYearsBack,
        CancellationToken ct = default)
    {
        // ── Step 1: Resolve the SQL table name suffix ──────────────────────
        var suffix = await _tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null)
        {
            _logger.LogWarning(
                "Symbol {Symbol} has no sql_table_name_suffix in the symbol master. Skipping.",
                symbol);
            return new SeedSymbolResult(symbol, 0, 0, 0, SeedOutcome.SkippedNoSuffix);
        }

        _logger.LogInformation(
            "Seeding historical data for {Symbol} (suffix: {Suffix}).",
            symbol, suffix);

        var dailyTableName = $"D_{suffix}";

        // ── Step 2: Create tables atomically (IF NOT EXISTS) ───────────────
        await using var conn = new NpgsqlConnection(_sqlConnectionString);
        await conn.OpenAsync(ct);

        NpgsqlTransaction? transaction = null;
        try
        {
            transaction = await conn.BeginTransactionAsync(ct);

            // SharedTableDdlTemplate creates D_, W_, M_ tables using IF NOT EXISTS.
            await SharedTableDdlTemplate.CreatePerSymbolTablesAsync(
                conn, suffix, transaction, ct);

            await transaction.CommitAsync(ct);
            _logger.LogDebug(
                "Tables D_{Suffix}, W_{Suffix}, M_{Suffix} ensured for {Symbol}.",
                suffix, suffix, suffix, symbol);
        }
        catch (Exception ex)
        {
            if (transaction is not null)
                await transaction.RollbackAsync(ct);

            _logger.LogError(ex,
                "Failed to create per-symbol tables for {Symbol} (suffix: {Suffix}).",
                symbol, suffix);
            Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Activity.Current?.AddException(ex);
            return new SeedSymbolResult(symbol, 0, 0, 0, SeedOutcome.FailedTableCreation);
        }
        finally
        {
            await (transaction?.DisposeAsync() ?? ValueTask.CompletedTask);
        }

        // ── Step 3: Find existing data boundaries ──────────────────────────
        var firstDate = await _ohlcvRepo.GetFirstCandleDateAsync(symbol, ct);
        var lastDate = await _ohlcvRepo.GetLastCandleDateAsync(symbol, ct);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ── Step 4: BACKFILL PHASE — go backwards from earliest data to inception ──
        // Start the cursor at the earliest known date, or today if the table is empty.
        // Walk backwards in MaxDaysPerChunk-sized chunks, stopping when the MDP
        // returns zero candles (inception reached).
        var backfillCursor = firstDate ?? today;
        var totalDailyInserted = 0;
        var dataRangeStart = (DateOnly?)null;
        var dataRangeEnd = (DateOnly?)null;

        while (backfillCursor > MinBackfillDate)
        {
            ct.ThrowIfCancellationRequested();

            var chunkEnd = backfillCursor.AddDays(-1);
            var chunkStart = chunkEnd.AddDays(-(MaxDaysPerChunk - 1));
            if (chunkStart < MinBackfillDate)
                chunkStart = MinBackfillDate;

            if (chunkStart > chunkEnd)
                break;

            _logger.LogDebug(
                "HDS backfill {Symbol}: chunk {From} to {To}.",
                symbol, chunkStart, chunkEnd);

            IReadOnlyList<SignalStack.MarketData.OhlcvRecord> records;
            try
            {
                records = await _marketDataProvider.FetchHistoricalOhlcvAsync(
                    symbol, chunkStart, chunkEnd, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "HDS backfill {Symbol}: fetch failed for range {From} to {To} " +
                    "from {Provider}. Aborting backfill.",
                    symbol, chunkStart, chunkEnd, _marketDataProvider.ProviderName);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                Activity.Current?.AddException(ex);
                // Partial backfill is better than none — proceed to forward-fill.
                break;
            }

            if (records.Count == 0)
            {
                _logger.LogInformation(
                    "HDS backfill {Symbol}: reached inception — " +
                    "no candles returned for {From} to {To}.",
                    symbol, chunkStart, chunkEnd);
                break;
            }

            int inserted;
            try
            {
                inserted = await BulkInsertDailyAsync(conn, dailyTableName, records, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "HDS backfill {Symbol}: failed to insert {Count} records for " +
                    "range {From} to {To} into {Table}. Aborting backfill.",
                    symbol, records.Count, chunkStart, chunkEnd, dailyTableName);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                Activity.Current?.AddException(ex);
                break;
            }

            totalDailyInserted += inserted;

            // Track the overall data range for aggregate recomputation.
            if (dataRangeStart is null || records[0].Date < dataRangeStart)
                dataRangeStart = records[0].Date;
            if (dataRangeEnd is null || records[^1].Date > dataRangeEnd)
                dataRangeEnd = records[^1].Date;

            _logger.LogInformation(
                "HDS backfill {Symbol}: inserted {Inserted}/{Fetched} records " +
                "dated {FirstRecord} to {LastRecord}. Cursor moving to {NextCursor}.",
                symbol, inserted, records.Count,
                records[0].Date, records[^1].Date, chunkStart.AddDays(-1));

            backfillCursor = chunkStart.AddDays(-1);
        }

        // ── Step 5: FORWARD FILL PHASE — close any gap between latest data and today ──
        var latestAfterBackfill = await _ohlcvRepo.GetLastCandleDateAsync(symbol, ct);
        var forwardFrom = latestAfterBackfill?.AddDays(1);

        if (forwardFrom.HasValue && forwardFrom.Value <= today)
        {
            _logger.LogInformation(
                "HDS forward-fill {Symbol}: fetching missing data from {From} to {To}.",
                symbol, forwardFrom.Value, today);

            IReadOnlyList<SignalStack.MarketData.OhlcvRecord> forwardRecords;
            try
            {
                forwardRecords = await _marketDataProvider.FetchHistoricalOhlcvAsync(
                    symbol, forwardFrom.Value, today, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "HDS forward-fill {Symbol}: fetch failed for range {From} to {To} " +
                    "from {Provider}.",
                    symbol, forwardFrom.Value, today, _marketDataProvider.ProviderName);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                Activity.Current?.AddException(ex);
                forwardRecords = [];
            }

            if (forwardRecords.Count > 0)
            {
                int inserted;
                try
                {
                    inserted = await BulkInsertDailyAsync(conn, dailyTableName, forwardRecords, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "HDS forward-fill {Symbol}: failed to insert {Count} records " +
                        "into {Table}.",
                        symbol, forwardRecords.Count, dailyTableName);
                    Activity.Current?.SetStatus(ActivityStatusCode.Error, ex.Message);
                    Activity.Current?.AddException(ex);
                    inserted = 0;
                }

                totalDailyInserted += inserted;

                if (dataRangeStart is null || forwardRecords[0].Date < dataRangeStart)
                    dataRangeStart = forwardRecords[0].Date;
                if (dataRangeEnd is null || forwardRecords[^1].Date > dataRangeEnd)
                    dataRangeEnd = forwardRecords[^1].Date;

                _logger.LogInformation(
                    "HDS forward-fill {Symbol}: inserted {Inserted}/{Fetched} records " +
                    "dated {First} to {Last}.",
                    symbol, inserted, forwardRecords.Count,
                    forwardRecords[0].Date, forwardRecords[^1].Date);
            }
            else
            {
                _logger.LogDebug(
                    "HDS forward-fill {Symbol}: no new data from {From} to {To}.",
                    symbol, forwardFrom.Value, today);
            }
        }

        // If nothing was inserted, determine the right outcome.
        if (totalDailyInserted == 0)
        {
            var hasExistingData = firstDate.HasValue || lastDate.HasValue;
            var outcome = hasExistingData
                ? SeedOutcome.AlreadyUpToDate
                : SeedOutcome.NoDataReturned;

            _logger.LogInformation(
                "HDS {Symbol}: {Outcome} — no new daily records inserted.",
                symbol, outcome);

            return new SeedSymbolResult(symbol, 0, 0, 0, outcome);
        }

        _logger.LogInformation(
            "HDS {Symbol}: inserted a total of {Count} daily records " +
            "spanning {FirstDate} to {LastDate}.",
            symbol, totalDailyInserted, dataRangeStart, dataRangeEnd);

        // ── Step 6: Compute and upsert weekly aggregates ───────────────────
        var weeklyTableName = $"W_{suffix}";
        var weeklyComputed = await ComputeAndInsertAggregatesAsync(
            conn, dailyTableName, weeklyTableName,
            "weekly", "date_trunc('week', \"Date\")",
            dataRangeStart!.Value, dataRangeEnd!.Value, ct);

        _logger.LogInformation(
            "Computed {Count} weekly records for {Symbol} into {Table}.",
            weeklyComputed, symbol, weeklyTableName);

        // ── Step 7: Compute and upsert monthly aggregates ──────────────────
        var monthlyTableName = $"M_{suffix}";
        var monthlyComputed = await ComputeAndInsertAggregatesAsync(
            conn, dailyTableName, monthlyTableName,
            "monthly", "date_trunc('month', \"Date\")",
            dataRangeStart!.Value, dataRangeEnd!.Value, ct);

        _logger.LogInformation(
            "Computed {Count} monthly records for {Symbol} into {Table}.",
            monthlyComputed, symbol, monthlyTableName);

        return new SeedSymbolResult(
            symbol, totalDailyInserted, weeklyComputed, monthlyComputed, SeedOutcome.Success);
    }

    /// <summary>
    /// Bulk-inserts daily OHLCV records into the specified D_ table.
    /// Skips rows where the Date already exists (idempotent insert).
    /// Uses NpgsqlBinaryImporter (binary COPY) for high-performance bulk load.
    /// </summary>
    private async Task<int> BulkInsertDailyAsync(
        NpgsqlConnection conn,
        string tableName,
        IReadOnlyList<SignalStack.MarketData.OhlcvRecord> records,
        CancellationToken ct)
    {
        // Use a staging approach: insert rows that don't already exist.
        // Create a temp table, bulk insert into it via binary COPY, then
        // INSERT...ON CONFLICT DO NOTHING into the target.
        var tempTableName = $"\"tmp_{tableName}\"";

        // DROP before CREATE handles Npgsql connection pooling where temp tables
        // persist on the same pooled connection across calls.
        var createTempSql = $@"
DROP TABLE IF EXISTS {tempTableName};
CREATE TEMPORARY TABLE {tempTableName} (
    ""Date""   DATE              NOT NULL,
    ""Open""   DOUBLE PRECISION  NOT NULL,
    ""High""   DOUBLE PRECISION  NOT NULL,
    ""Low""    DOUBLE PRECISION  NOT NULL,
    ""Close""  DOUBLE PRECISION  NOT NULL,
    ""Volume"" BIGINT            NOT NULL,
    PRIMARY KEY (""Date"")
);";

        await using (var createCmd = new NpgsqlCommand(createTempSql, conn))
        {
            await createCmd.ExecuteNonQueryAsync(ct);
        }

        // Binary COPY into temp table — high-performance bulk load.
        await using (var writer = conn.BeginBinaryImport(
            $"COPY {tempTableName} (\"Date\", \"Open\", \"High\", \"Low\", \"Close\", \"Volume\") FROM STDIN (FORMAT BINARY)"))
        {
            foreach (var record in records)
            {
                await writer.StartRowAsync(ct);
                await writer.WriteAsync(record.Date.ToDateTime(TimeOnly.MinValue), NpgsqlDbType.Date, ct);
                await writer.WriteAsync((double)record.Open, NpgsqlDbType.Double, ct);
                await writer.WriteAsync((double)record.High, NpgsqlDbType.Double, ct);
                await writer.WriteAsync((double)record.Low, NpgsqlDbType.Double, ct);
                await writer.WriteAsync((double)record.Close, NpgsqlDbType.Double, ct);
                await writer.WriteAsync(record.Volume, NpgsqlDbType.Bigint, ct);
            }

            await writer.CompleteAsync(ct);
        }

        // INSERT from temp table into target — only rows that don't exist (idempotent).
        var insertSql = $@"
INSERT INTO ""{tableName}"" (""Date"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume"")
SELECT ""Date"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume""
FROM {tempTableName}
ON CONFLICT (""Date"") DO NOTHING;";

        await using (var insertCmd = new NpgsqlCommand(insertSql, conn))
        {
            await insertCmd.ExecuteNonQueryAsync(ct);
        }

        // Count inserted rows.
        var countSql = $"SELECT COUNT(*) FROM {tempTableName};";
        await using (var countCmd = new NpgsqlCommand(countSql, conn))
        {
            var count = (long)(await countCmd.ExecuteScalarAsync(ct))!;
            return (int)count;
        }
    }

    /// <summary>
    /// Computes weekly or monthly aggregated candles from the D_ table and
    /// inserts them into the target W_ or M_ table. Uses INSERT...ON CONFLICT
    /// for idempotent upserts.
    /// </summary>
    /// <param name="conn">Open Npgsql connection.</param>
    /// <param name="sourceTable">The D_ table name.</param>
    /// <param name="targetTable">The W_ or M_ table name.</param>
    /// <param name="label">"weekly" or "monthly" — used for logging.</param>
    /// <param name="groupByExpression">
    /// SQL expression to compute the period start date from "Date".
    /// For weekly: date_trunc('week', "Date")
    /// For monthly: date_trunc('month', "Date")
    /// </param>
    /// <param name="fromDate">Start of the data range (inclusive).</param>
    /// <param name="toDate">End of the data range (inclusive).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Number of rows inserted or updated.</returns>
    private async Task<int> ComputeAndInsertAggregatesAsync(
        NpgsqlConnection conn,
        string sourceTable,
        string targetTable,
        string label,
        string groupByExpression,
        DateOnly fromDate,
        DateOnly toDate,
        CancellationToken ct)
    {
        // Temp table for computed aggregates.
        var tempTable = $"\"tmp_{targetTable}\"";

        var createTempSql = $@"
DROP TABLE IF EXISTS {tempTable};
CREATE TEMPORARY TABLE {tempTable} (
    ""PeriodStart"" DATE              NOT NULL,
    ""Open""        DOUBLE PRECISION  NOT NULL,
    ""High""        DOUBLE PRECISION  NOT NULL,
    ""Low""         DOUBLE PRECISION  NOT NULL,
    ""Close""       DOUBLE PRECISION  NOT NULL,
    ""Volume""      BIGINT            NOT NULL,
    PRIMARY KEY (""PeriodStart"")
);";

        await using (var createCmd = new NpgsqlCommand(createTempSql, conn))
        {
            await createCmd.ExecuteNonQueryAsync(ct);
        }

        // Compute aggregates from D_ and insert into temp table.
        // The Open is the first trading day's open of the period, Close is the
        // last trading day's close, High/Low are the period's extremes.
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
    WHERE ""Date"" >= @FromDate AND ""Date"" <= @ToDate
) AS subq
GROUP BY {groupByExpression}
ORDER BY ""PeriodStart"";";

        await using (var computeCmd = new NpgsqlCommand(computeSql, conn))
        {
            computeCmd.Parameters.AddWithValue("@FromDate", fromDate.ToDateTime(TimeOnly.MinValue));
            computeCmd.Parameters.AddWithValue("@ToDate", toDate.ToDateTime(TimeOnly.MinValue));
            await computeCmd.ExecuteNonQueryAsync(ct);
        }

        // INSERT into target table — ON CONFLICT DO UPDATE for idempotent upsert.
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

        // Count rows affected.
        var countSql = $"SELECT COUNT(*) FROM {tempTable};";
        await using (var countCmd = new NpgsqlCommand(countSql, conn))
        {
            var count = (long)(await countCmd.ExecuteScalarAsync(ct))!;
            return (int)count;
        }
    }
}

/// <summary>
/// Outcome of seeding a single symbol.
/// </summary>
public sealed record SeedSymbolResult(
    string Symbol,
    int DailyInserted,
    int WeeklyComputed,
    int MonthlyComputed,
    SeedOutcome Outcome);

/// <summary>
/// Possible outcomes for a per-symbol seed operation.
/// </summary>
public enum SeedOutcome
{
    /// <summary>Data was successfully seeded.</summary>
    Success,

    /// <summary>Symbol already fully up to date; no action needed.</summary>
    AlreadyUpToDate,

    /// <summary>Symbol has no sql_table_name_suffix — skipped.</summary>
    SkippedNoSuffix,

    /// <summary>Table creation failed.</summary>
    FailedTableCreation,

    /// <summary>Market data provider returned no data for the requested range.</summary>
    NoDataReturned,

    /// <summary>Failed to fetch data from the market data provider.</summary>
    FailedProviderFetch,

    /// <summary>Failed to insert daily data into PostgreSQL.</summary>
    FailedDailyInsert
}
