using Microsoft.Extensions.Logging;
using Npgsql;

namespace SignalStack.Storage.Historical;

/// <summary>
/// PostgreSQL implementation of <see cref="IOhlcvRepository"/>.
///
/// REQ-HIST-001/002: queries per-symbol D_, W_, M_ tables with Date/Open/High/Low/Close/Volume.
/// REQ-HIST-004: tables are referenced using <c>sql_table_name_suffix</c> via <see cref="ISymbolTableMapping"/>.
/// REQ-HIST-011: all table names resolved through the shared mapping service.
/// REQ-TIMEFRAME-005: W_ and M_ tables are pre-computed; D_ used for rolling candles.
/// </summary>
public sealed class SqlOhlcvRepository : IOhlcvRepository
{
    private readonly string _connectionString;
    private readonly ISymbolTableMapping _tableMapping;
    private readonly ILogger<SqlOhlcvRepository> _logger;

    public SqlOhlcvRepository(
        string connectionString,
        ISymbolTableMapping tableMapping,
        ILogger<SqlOhlcvRepository> logger)
    {
        _connectionString = connectionString;
        _tableMapping = tableMapping;
        _logger = logger;
    }

    public async Task<List<OhlcvRecord>> GetDailyAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var suffix = await _tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null) return [];

        return await QueryOhlcvAsync($"D_{suffix}", from, to, ct);
    }

    public async Task<List<OhlcvRecord>> GetWeeklyAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var suffix = await _tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null) return [];

        return await QueryOhlcvAsync($"W_{suffix}", from, to, ct);
    }

    public async Task<List<OhlcvRecord>> GetMonthlyAsync(
        string symbol, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var suffix = await _tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null) return [];

        return await QueryOhlcvAsync($"M_{suffix}", from, to, ct);
    }

    public async Task<List<OhlcvRecord>> GetRollingAsync(
        string symbol, int windowSize, DateOnly to, CancellationToken ct = default)
    {
        if (windowSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(windowSize), "Window size must be positive.");

        var suffix = await _tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null) return [];

        var tableName = $"D_{suffix}";

        // Rolling candles: fetch the last N sessions and build one OHLCV record.
        // REQ-TIMEFRAME-003: the opening bar is the oldest included trading session,
        // and the closing bar is the most recent trading session.
        // REQ-HIST-010a: sessions determined by the NSE trading calendar.
        var sql = $@"
SELECT ""Date"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume""
FROM ""{tableName}""
WHERE ""Date"" <= @ToDate
ORDER BY ""Date"" DESC
LIMIT @WindowSize";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@WindowSize", windowSize);
        cmd.Parameters.AddWithValue("@ToDate", to.ToDateTime(TimeOnly.MinValue));

        var records = new List<OhlcvRecord>(windowSize);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            records.Add(ReadOhlcv(reader));
        }

        // Records are in descending order; reverse to ascending for the final result.
        records.Reverse();

        if (records.Count == 0) return [];
        if (records.Count == windowSize)
        {
            // Build a single rolling candle from the window.
            var first = records[0];
            var last = records[^1];
            return
            [
                new OhlcvRecord(
                    Date: first.Date,
                    Open: first.Open,
                    High: records.Max(r => r.High),
                    Low: records.Min(r => r.Low),
                    Close: last.Close,
                    Volume: records.Sum(r => r.Volume)
                )
            ];
        }

        // Fewer sessions than windowSize — return what we have as a partial window.
        // This preserves data for recently-listed symbols.
        return records;
    }

    public async Task<DateOnly?> GetLastCandleDateAsync(string symbol, CancellationToken ct = default)
    {
        var suffix = await _tableMapping.GetSuffixAsync(symbol, ct);
        if (suffix is null) return null;

        var tableName = $"D_{suffix}";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // First check if the table exists.
        var tableExistsQuery = @"
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.TABLES
    WHERE TABLE_SCHEMA = 'public' AND TABLE_NAME = @TableName
) THEN 1 ELSE 0 END";

        await using var existsCmd = new NpgsqlCommand(tableExistsQuery, conn);
        existsCmd.Parameters.AddWithValue("@TableName", tableName);
        var exists = (int)(await existsCmd.ExecuteScalarAsync(ct))! == 1;

        if (!exists) return null;

        // Query most recent Date.
        var dateQuery = $"SELECT MAX(\"Date\") FROM \"{tableName}\"";
        await using var dateCmd = new NpgsqlCommand(dateQuery, conn);
        var result = await dateCmd.ExecuteScalarAsync(ct);

        if (result is null || result == DBNull.Value) return null;
        return DateOnly.FromDateTime(Convert.ToDateTime(result));
    }

    private async Task<List<OhlcvRecord>> QueryOhlcvAsync(
        string tableName, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var sql = $@"
SELECT ""Date"", ""Open"", ""High"", ""Low"", ""Close"", ""Volume""
FROM ""{tableName}""
WHERE ""Date"" >= @FromDate AND ""Date"" <= @ToDate
ORDER BY ""Date"" ASC";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@FromDate", from.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@ToDate", to.ToDateTime(TimeOnly.MinValue));

        var results = new List<OhlcvRecord>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            results.Add(ReadOhlcv(reader));
        }

        return results;
    }

    private static OhlcvRecord ReadOhlcv(NpgsqlDataReader reader)
    {
        return new OhlcvRecord(
            Date: DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("Date"))),
            Open: reader.GetDouble(reader.GetOrdinal("Open")),
            High: reader.GetDouble(reader.GetOrdinal("High")),
            Low: reader.GetDouble(reader.GetOrdinal("Low")),
            Close: reader.GetDouble(reader.GetOrdinal("Close")),
            Volume: (long)reader.GetInt64(reader.GetOrdinal("Volume"))
        );
    }
}
