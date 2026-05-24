using Npgsql;

namespace SignalStack.SqlMigrations;

/// <summary>
/// Shared DDL template for per-symbol OHLCV tables (D_, W_, M_).
///
/// REQ-HIST-009a(c): the column definitions and index specifications for the three
/// per-symbol tables must be held as a single shared DDL template so that a future
/// change to the per-symbol table shape is a single edit.
///
/// This template is used by:
/// - HistoricDataSeed (HDS) when materialising tables for new symbols
/// - REQ-MIGRATION-004 batched migrations for fleet-wide schema evolution
///
/// REQ-HIST-002: each OHLCV table contains Date, Open, High, Low, Close, Volume.
/// This schema applies identically to D_, W_, and M_ tables.
/// </summary>
public static class SharedTableDdlTemplate
{
    /// <summary>
    /// Returns the CREATE TABLE DDL for a per-symbol OHLCV table with the given name.
    /// Uses IF NOT EXISTS semantics per REQ-HIST-009a(a).
    /// </summary>
    /// <param name="tableName">The fully-qualified table name (e.g. "D_RELIANCE").</param>
    /// <returns>The DDL string.</returns>
    public static string CreateTableIfNotExists(string tableName)
    {
        return $@"
CREATE TABLE IF NOT EXISTS ""public"".""{tableName}"" (
    ""Date""   DATE              NOT NULL,
    ""Open""   DOUBLE PRECISION  NOT NULL,
    ""High""   DOUBLE PRECISION  NOT NULL,
    ""Low""    DOUBLE PRECISION  NOT NULL,
    ""Close""  DOUBLE PRECISION  NOT NULL,
    ""Volume"" BIGINT            NOT NULL,
    PRIMARY KEY (""Date"")
);

CREATE INDEX IF NOT EXISTS ""IX_{tableName}_Date"" ON ""public"".""{tableName}"" (""Date"");";
    }

    /// <summary>
    /// Creates the three per-symbol tables (D_, W_, M_) for the given suffix
    /// inside a single database transaction per REQ-HIST-009a(b).
    /// </summary>
    /// <param name="connection">An open NpgsqlConnection.</param>
    /// <param name="sqlTableNameSuffix">The symbol's sql_table_name_suffix.</param>
    /// <param name="transaction">The database transaction for atomicity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task CreatePerSymbolTablesAsync(
        NpgsqlConnection connection,
        string sqlTableNameSuffix,
        NpgsqlTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var dTable = $"D_{sqlTableNameSuffix}";
        var wTable = $"W_{sqlTableNameSuffix}";
        var mTable = $"M_{sqlTableNameSuffix}";

        foreach (var tableName in new[] { dTable, wTable, mTable })
        {
            var ddl = CreateTableIfNotExists(tableName);
            await using var cmd = new NpgsqlCommand(ddl, connection, transaction);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
