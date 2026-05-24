using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using SignalStack.Api.Historical;
using SignalStack.Api.Universe;
using SignalStack.MarketData;
using SignalStack.SqlMigrations;

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
    /// Seeds historical data for a single symbol. Idempotent and resumable.
    /// </summary>
    /// <param name="symbol">NSE trading symbol (e.g. "RELIANCE").</param>
    /// <param name="historicalYearsBack">Number of years of history to fetch.</param>
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
            return new SeedSymbolResult(symbol, 0, 0, 0, SeedOutcome.FailedTableCreation);
        }
        finally
        {
            await (transaction?.DisposeAsync() ?? ValueTask.CompletedTask);
        }

        // ── Step 3: Determine resumption point ─────────────────────────────
        var lastDate = await _ohlcvRepo.GetLastCandleDateAsync(symbol, ct);
        var fromDate = lastDate?.AddDays(1)
            ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-historicalYearsBack));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        if (fromDate > today)
        {
            _logger.LogInformation(
                "Symbol {Symbol} is already fully seeded up to {LastDate}. Nothing to do.",
                symbol, lastDate);
            return new SeedSymbolResult(symbol, 0, 0, 0, SeedOutcome.AlreadyUpToDate);
        }

        _logger.LogInformation(
            "Fetching historical data for {Symbol} from {FromDate} to {ToDate} "
            + "(resumed from last candle: {LastDate}).",
            symbol, fromDate, today, lastDate?.ToString() ?? "(none)");

        // ── Step 4: Fetch historical daily OHLCV from the MDP ──────────────
        IReadOnlyList<SignalStack.MarketData.OhlcvRecord> providerRecords;
        try
        {
            providerRecords = await _marketDataProvider.FetchHistoricalOhlcvAsync(
                symbol, fromDate, today, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to fetch historical data for {Symbol} from {Provider}.",
                symbol, _marketDataProvider.ProviderName);
            return new SeedSymbolResult(symbol, 0, 0, 0, SeedOutcome.FailedProviderFetch);
        }

        if (providerRecords.Count == 0)
        {
            _logger.LogInformation(
                "No historical data returned for {Symbol} from {FromDate} to {ToDate}.",
                symbol, fromDate, today);
            return new SeedSymbolResult(symbol, 0, 0, 0, SeedOutcome.NoDataReturned);
        }

        _logger.LogInformation(
            "Fetched {Count} daily records for {Symbol} from {FirstDate} to {LastDate}.",
            providerRecords.Count, symbol,
            providerRecords[0].Date, providerRecords[^1].Date);

        // ── Step 5: Insert daily data into D_ table ────────────────────────
        var dailyTableName = $"D_{suffix}";
        int dailyInserted;

        try
        {
            dailyInserted = await BulkInsertDailyAsync(
                conn, dailyTableName, providerRecords, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to insert daily data for {Symbol} into {Table}.",
                symbol, dailyTableName);
            return new SeedSymbolResult(symbol, providerRecords.Count, 0, 0, SeedOutcome.FailedDailyInsert);
        }

        _logger.LogInformation(
            "Inserted {Count} daily records into {Table} for {Symbol}.",
            dailyInserted, dailyTableName, symbol);

        // ── Step 6: Compute and upsert weekly aggregates ───────────────────
        var weeklyTableName = $"W_{suffix}";
        var weeklyComputed = await ComputeAndInsertAggregatesAsync(
            conn, dailyTableName, weeklyTableName,
            "weekly", "date_trunc('week', \"Date\")",
            providerRecords[0].Date, providerRecords[^1].Date, ct);

        _logger.LogInformation(
            "Computed {Count} weekly records for {Symbol} into {Table}.",
            weeklyComputed, symbol, weeklyTableName);

        // ── Step 7: Compute and upsert monthly aggregates ──────────────────
        var monthlyTableName = $"M_{suffix}";
        var monthlyComputed = await ComputeAndInsertAggregatesAsync(
            conn, dailyTableName, monthlyTableName,
            "monthly", "date_trunc('month', \"Date\")",
            providerRecords[0].Date, providerRecords[^1].Date, ct);

        _logger.LogInformation(
            "Computed {Count} monthly records for {Symbol} into {Table}.",
            monthlyComputed, symbol, monthlyTableName);

        return new SeedSymbolResult(
            symbol, dailyInserted, weeklyComputed, monthlyComputed, SeedOutcome.Success);
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

        var createTempSql = $@"
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
