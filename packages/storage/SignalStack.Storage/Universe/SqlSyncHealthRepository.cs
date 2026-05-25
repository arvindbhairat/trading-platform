using Microsoft.Extensions.Logging;
using Npgsql;

namespace SignalStack.Storage.Universe;

/// <summary>
/// PostgreSQL implementation of <see cref="ISyncHealthRepository"/>.
/// Queries per-symbol D_{suffix} tables for the most recent candle date.
/// REQ-UNIV-015a: data sync health check.
/// </summary>
public sealed class SqlSyncHealthRepository : ISyncHealthRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqlSyncHealthRepository> _logger;

    public SqlSyncHealthRepository(
        string connectionString,
        ILogger<SqlSyncHealthRepository> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public async Task<List<SymbolSyncHealth>> GetSyncHealthAsync(
        List<string> suffixes, CancellationToken ct = default)
    {
        if (suffixes.Count == 0) return [];

        var results = new List<SymbolSyncHealth>(suffixes.Count);

        // Check each suffix's D_ table for the most recent date.
        // We query in batches to avoid overwhelming the database.
        const int batchSize = 50;
        for (var i = 0; i < suffixes.Count; i += batchSize)
        {
            var batch = suffixes.Skip(i).Take(batchSize).ToList();
            var batchResults = await QueryBatchAsync(batch, ct);
            results.AddRange(batchResults);
        }

        return results;
    }

    private async Task<List<SymbolSyncHealth>> QueryBatchAsync(
        List<string> suffixes, CancellationToken ct)
    {
        var results = new List<SymbolSyncHealth>(suffixes.Count);

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var suffix in suffixes)
        {
            var tableName = $"D_{suffix}";
            try
            {
                // First check if the table exists.
                var tableExistsQuery = @"
                    SELECT CASE WHEN EXISTS (
                        SELECT 1 FROM INFORMATION_SCHEMA.TABLES
                        WHERE TABLE_SCHEMA = 'public' AND TABLE_NAME = @TableName
                    ) THEN 1 ELSE 0 END";

                await using var existsCmd = new NpgsqlCommand(tableExistsQuery, conn);
                existsCmd.Parameters.AddWithValue("@TableName", tableName);
                var exists = (int)(await existsCmd.ExecuteScalarAsync(ct))! == 1;

                if (!exists)
                {
                    results.Add(new SymbolSyncHealth
                    {
                        Suffix = suffix,
                        LastCandleDate = null
                    });
                    continue;
                }

                // Query the most recent Date.
                var dateQuery = $"SELECT MAX(\"Date\") FROM \"{tableName}\"";
                await using var dateCmd = new NpgsqlCommand(dateQuery, conn);
                var result = await dateCmd.ExecuteScalarAsync(ct);
                DateTime? lastDate = null;
                if (result is not null && result != DBNull.Value)
                {
                    lastDate = Convert.ToDateTime(result);
                }

                results.Add(new SymbolSyncHealth
                {
                    Suffix = suffix,
                    LastCandleDate = lastDate
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to query sync health for suffix {Suffix}, table {TableName}",
                    suffix, tableName);

                // If the table doesn't exist or query fails, treat as no data.
                results.Add(new SymbolSyncHealth
                {
                    Suffix = suffix,
                    LastCandleDate = null
                });
            }
        }

        return results;
    }
}
