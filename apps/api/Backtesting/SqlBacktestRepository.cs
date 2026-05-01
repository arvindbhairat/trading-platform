using System.Data;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// SQL Server persistence for backtest results using BT_-prefixed tables.
/// REQ-BTSTORE-001: persisted to SQL Server backtest database.
/// REQ-BTSTORE-002: BT_ prefix distinguishes from market data tables.
/// </summary>
public sealed class SqlBacktestRepository : IBacktestRepository
{
    private readonly string _connectionString;
    private readonly ILogger<SqlBacktestRepository> _logger;

    public SqlBacktestRepository(string connectionString, ILogger<SqlBacktestRepository> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger;
    }

    public async Task SaveRunHeaderAsync(BacktestRunResult result, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO BT_RunHeaders (
                run_id, signal_type, signal_parameters, rme_profile_snapshot,
                timeframe, slippage, commission_pct, symbol_universe,
                date_range_start, date_range_end, run_timestamp
            ) VALUES (
                @RunId, @SignalType, @SignalParams, @RmeProfile,
                @Timeframe, @Slippage, @CommissionPct, '{}',
                @DateStart, @DateEnd, @RunTimestamp
            )";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RunId", result.RunId);
        cmd.Parameters.AddWithValue("@SignalType", result.SignalType);
        cmd.Parameters.AddWithValue("@SignalParams", result.SignalSubscriptionVersionId ?? "");
        cmd.Parameters.AddWithValue("@RmeProfile", "{}");
        cmd.Parameters.AddWithValue("@Timeframe", result.Timeframe);
        cmd.Parameters.AddWithValue("@Slippage", result.Slippage);
        cmd.Parameters.AddWithValue("@CommissionPct", result.CommissionPct);
        cmd.Parameters.AddWithValue("@DateStart", result.DateRangeStart.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@DateEnd", result.DateRangeEnd.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@RunTimestamp", result.RunTimestamp);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SaveTradesAsync(Guid runId, List<BacktestTrade> trades, CancellationToken ct = default)
    {
        if (trades.Count == 0) return;

        const string sql = @"
            INSERT INTO BT_Trades (
                run_id, symbol, entry_date, exit_date, entry_price, exit_price,
                stop_at_entry, exit_reason, r_multiple, gross_pnl, net_pnl
            ) VALUES (
                @RunId, @Symbol, @EntryDate, @ExitDate, @EntryPrice, @ExitPrice,
                @StopAtEntry, @ExitReason, @RMultiple, @GrossPnl, @NetPnl
            )";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var trade in trades)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@RunId", runId);
            cmd.Parameters.AddWithValue("@Symbol", trade.Symbol);
            cmd.Parameters.AddWithValue("@EntryDate", trade.EntryDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@ExitDate", (object?)trade.ExitDate?.ToDateTime(TimeOnly.MinValue) ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@EntryPrice", trade.EntryPrice);
            cmd.Parameters.AddWithValue("@ExitPrice", (object?)trade.ExitPrice ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@StopAtEntry", trade.StopAtEntry);
            cmd.Parameters.AddWithValue("@ExitReason", (object?)trade.ExitReason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@RMultiple", trade.RMultiple);
            cmd.Parameters.AddWithValue("@GrossPnl", trade.GrossPnL);
            cmd.Parameters.AddWithValue("@NetPnl", trade.NetPnL);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task SavePositionEventsAsync(Guid runId, List<BacktestPositionEvent> events, CancellationToken ct = default)
    {
        if (events.Count == 0) return;

        const string sql = @"
            INSERT INTO BT_PositionLifecycle (
                run_id, symbol, event_type, event_date, rme_state, peak_r_multiple
            ) VALUES (
                @RunId, @Symbol, @EventType, @EventDate, @RmeState, @PeakR
            )";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var evt in events)
        {
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@RunId", runId);
            cmd.Parameters.AddWithValue("@Symbol", evt.Symbol);
            cmd.Parameters.AddWithValue("@EventType", evt.EventType);
            cmd.Parameters.AddWithValue("@EventDate", evt.EventDate.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@RmeState", (object?)evt.RmeState ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PeakR", (object?)evt.PeakRMultiple ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task SavePortfolioPerformanceAsync(BacktestRunResult result, CancellationToken ct = default)
    {
        const string sql = @"
            INSERT INTO BT_PortfolioPerformance (
                run_id, equity_curve, xirr, sharpe_ratio, max_drawdown_pct,
                win_rate_pct, expectancy, profit_factor,
                starting_equity, ending_equity
            ) VALUES (
                @RunId, @EquityCurve, @Xirr, @SharpeRatio, @MaxDrawdown,
                @WinRate, @Expectancy, @ProfitFactor,
                @StartingEquity, @EndingEquity
            )";

        var equityCurveJson = JsonSerializer.Serialize(result.EquityCurve.Select(e => new
        {
            date = e.Date.ToString("yyyy-MM-dd"),
            equity = e.Equity
        }));

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RunId", result.RunId);
        cmd.Parameters.AddWithValue("@EquityCurve", equityCurveJson);
        cmd.Parameters.AddWithValue("@Xirr", (object?)DBNull.Value); // Not computed for basic backtest
        cmd.Parameters.AddWithValue("@SharpeRatio", result.SharpeRatio);
        cmd.Parameters.AddWithValue("@MaxDrawdown", result.MaxDrawdownPct);
        cmd.Parameters.AddWithValue("@WinRate", result.WinRatePct);
        cmd.Parameters.AddWithValue("@Expectancy", result.ExpectancyInR);
        cmd.Parameters.AddWithValue("@ProfitFactor", result.ProfitFactor);
        cmd.Parameters.AddWithValue("@StartingEquity", result.StartingEquity);
        cmd.Parameters.AddWithValue("@EndingEquity", result.EndingEquity);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<BacktestRunResult?> GetRunAsync(Guid runId, CancellationToken ct = default)
    {
        // Full result deserialization from BT_ tables will be implemented in P4-T4
        // when the result UI is built. For P4-T3 the engine + persistence + metrics
        // are the focus.
        _logger.LogWarning("GetRunAsync not fully implemented — returns null for reconstruction.");
        return null;
    }

    public async Task<List<BacktestRunHeader>> ListRunsAsync(
        int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT TOP (@Limit) rh.run_id, rh.signal_type, rh.timeframe,
                   rh.date_range_start, rh.date_range_end,
                   rh.run_timestamp,
                   pp.starting_equity, pp.ending_equity,
                   pp.win_rate_pct, pp.expectancy,
                   (SELECT COUNT(*) FROM BT_Trades t WHERE t.run_id = rh.run_id) AS total_trades
            FROM BT_RunHeaders rh
            LEFT JOIN BT_PortfolioPerformance pp ON pp.run_id = rh.run_id
            WHERE rh.run_id NOT IN (
                SELECT run_id FROM (
                    SELECT run_id, ROW_NUMBER() OVER (ORDER BY run_timestamp DESC) AS rn
                    FROM BT_RunHeaders
                ) sub WHERE rn <= @Offset
            )
            ORDER BY rh.run_timestamp DESC";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Limit", limit);
        cmd.Parameters.AddWithValue("@Offset", offset);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var headers = new List<BacktestRunHeader>();
        while (await reader.ReadAsync(ct))
        {
            headers.Add(new BacktestRunHeader
            {
                RunId = reader.GetGuid(reader.GetOrdinal("run_id")),
                SignalType = reader.GetString(reader.GetOrdinal("signal_type")),
                Timeframe = reader.GetString(reader.GetOrdinal("timeframe")),
                DateRangeStart = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("date_range_start"))),
                DateRangeEnd = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("date_range_end"))),
                RunTimestamp = reader.GetDateTime(reader.GetOrdinal("run_timestamp")),
                StartingEquity = reader.IsDBNull(reader.GetOrdinal("starting_equity")) ? 0 : reader.GetDecimal(reader.GetOrdinal("starting_equity")),
                EndingEquity = reader.IsDBNull(reader.GetOrdinal("ending_equity")) ? 0 : reader.GetDecimal(reader.GetOrdinal("ending_equity")),
                TotalTrades = reader.IsDBNull(reader.GetOrdinal("total_trades")) ? 0 : reader.GetInt32(reader.GetOrdinal("total_trades")),
                ExpectancyInR = reader.IsDBNull(reader.GetOrdinal("expectancy")) ? 0 : reader.GetDecimal(reader.GetOrdinal("expectancy")),
            });
        }

        return headers;
    }
}
