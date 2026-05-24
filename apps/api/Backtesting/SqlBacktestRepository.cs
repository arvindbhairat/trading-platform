using System.Data;
using System.Text.Json;
using Npgsql;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// PostgreSQL persistence for backtest results using BT_-prefixed tables.
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
                date_range_start, date_range_end, run_timestamp,
                metrics_json, starting_equity, ending_equity,
                total_return_pct, total_return_absolute,
                sortino_ratio, recovery_factor, average_r,
                adjusted_expectancy, max_drawdown_in_r,
                low_confidence, universe_coverage_pct,
                total_trades, winning_trades, losing_trades,
                skipped_sessions, total_sessions,
                survivorship_bias_discount_pct, raw_expectancy,
                rme_config_json
            ) VALUES (
                @RunId, @SignalType, @SignalParams, @RmeProfile,
                @Timeframe, @Slippage, @CommissionPct, '{}',
                @DateStart, @DateEnd, @RunTimestamp,
                @MetricsJson, @StartingEquity, @EndingEquity,
                @TotalReturnPct, @TotalReturnAbsolute,
                @SortinoRatio, @RecoveryFactor, @AverageR,
                @AdjustedExpectancy, @MaxDrawdownInR,
                @LowConfidence, @UniverseCoveragePct,
                @TotalTrades, @WinningTrades, @LosingTrades,
                @SkippedSessions, @TotalSessions,
                @SurvivorshipDiscountPct, @RawExpectancy,
                @RmeConfigJson
            )";

        var metricsJson = JsonSerializer.Serialize(new
        {
            result.SharpeRatio,
            result.SortinoRatio,
            result.RecoveryFactor,
            result.AverageR,
            result.ExpectancyInR,
            result.AdjustedExpectancy,
            result.MaxDrawdownPct,
            result.MaxDrawdownInR,
            result.TotalReturnPct,
            result.TotalReturnAbsolute,
            result.WinRatePct,
            result.ProfitFactor,
            result.LowConfidence,
            result.UniverseCoveragePct,
            result.SkippedSessions,
            result.TotalSessions,
            result.SurvivorshipBiasDiscountPct,
            result.RawExpectancy,
            equity_curve = result.EquityCurve.Select(e => new { date = e.Date.ToString("yyyy-MM-dd"), equity = e.Equity })
        });

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RunId", result.RunId);
        cmd.Parameters.AddWithValue("@SignalType", result.SignalType);
        cmd.Parameters.AddWithValue("@SignalParams", result.SignalSubscriptionVersionId ?? "");
        cmd.Parameters.AddWithValue("@RmeProfile", result.RmeConfigurationJson ?? "{}");
        cmd.Parameters.AddWithValue("@Timeframe", result.Timeframe);
        cmd.Parameters.AddWithValue("@Slippage", result.Slippage);
        cmd.Parameters.AddWithValue("@CommissionPct", result.CommissionPct);
        cmd.Parameters.AddWithValue("@DateStart", result.DateRangeStart.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@DateEnd", result.DateRangeEnd.ToDateTime(TimeOnly.MinValue));
        cmd.Parameters.AddWithValue("@RunTimestamp", result.RunTimestamp);
        cmd.Parameters.AddWithValue("@MetricsJson", metricsJson);
        cmd.Parameters.AddWithValue("@StartingEquity", result.StartingEquity);
        cmd.Parameters.AddWithValue("@EndingEquity", result.EndingEquity);
        cmd.Parameters.AddWithValue("@TotalReturnPct", result.TotalReturnPct);
        cmd.Parameters.AddWithValue("@TotalReturnAbsolute", result.TotalReturnAbsolute);
        cmd.Parameters.AddWithValue("@SortinoRatio", result.SortinoRatio);
        cmd.Parameters.AddWithValue("@RecoveryFactor", result.RecoveryFactor);
        cmd.Parameters.AddWithValue("@AverageR", result.AverageR);
        cmd.Parameters.AddWithValue("@AdjustedExpectancy", (object?)result.AdjustedExpectancy ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@MaxDrawdownInR", result.MaxDrawdownInR);
        cmd.Parameters.AddWithValue("@LowConfidence", result.LowConfidence);
        cmd.Parameters.AddWithValue("@UniverseCoveragePct", result.UniverseCoveragePct);
        cmd.Parameters.AddWithValue("@TotalTrades", result.TotalTrades);
        cmd.Parameters.AddWithValue("@WinningTrades", result.WinningTrades);
        cmd.Parameters.AddWithValue("@LosingTrades", result.LosingTrades);
        cmd.Parameters.AddWithValue("@SkippedSessions", result.SkippedSessions);
        cmd.Parameters.AddWithValue("@TotalSessions", result.TotalSessions);
        cmd.Parameters.AddWithValue("@SurvivorshipDiscountPct", result.SurvivorshipBiasDiscountPct);
        cmd.Parameters.AddWithValue("@RawExpectancy", result.RawExpectancy);
        cmd.Parameters.AddWithValue("@RmeConfigJson", result.RmeConfigurationJson ?? "{}");
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

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var trade in trades)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
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

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        foreach (var evt in events)
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
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

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@RunId", result.RunId);
        cmd.Parameters.AddWithValue("@EquityCurve", equityCurveJson);
        cmd.Parameters.AddWithValue("@Xirr", DBNull.Value); // Not computed for basic backtest
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
        const string headerSql = @"
            SELECT run_id, signal_type, signal_parameters, rme_profile_snapshot,
                   timeframe, slippage, commission_pct,
                   date_range_start, date_range_end, run_timestamp,
                   metrics_json, starting_equity, ending_equity,
                   total_return_pct, total_return_absolute,
                   sortino_ratio, recovery_factor, average_r,
                   adjusted_expectancy, max_drawdown_in_r,
                   low_confidence, universe_coverage_pct,
                   total_trades, winning_trades, losing_trades,
                   skipped_sessions, total_sessions,
                   survivorship_bias_discount_pct, raw_expectancy,
                   rme_config_json
            FROM BT_RunHeaders
            WHERE run_id = @RunId";

        const string tradesSql = @"
            SELECT symbol, entry_date, exit_date, entry_price, exit_price,
                   stop_at_entry, exit_reason, r_multiple, gross_pnl, net_pnl
            FROM BT_Trades
            WHERE run_id = @RunId
            ORDER BY entry_date";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        // Read run header
        await using var headerCmd = new NpgsqlCommand(headerSql, conn);
        headerCmd.Parameters.AddWithValue("@RunId", runId);
        await using var headerReader = await headerCmd.ExecuteReaderAsync(ct);

        if (!await headerReader.ReadAsync(ct))
        {
            _logger.LogWarning("Backtest run {RunId} not found in BT_RunHeaders.", runId);
            return null;
        }

        var result = ReadRunHeader(headerReader);
        await headerReader.CloseAsync();

        // Read trades
        await using var tradesCmd = new NpgsqlCommand(tradesSql, conn);
        tradesCmd.Parameters.AddWithValue("@RunId", runId);
        await using var tradesReader = await tradesCmd.ExecuteReaderAsync(ct);

        var trades = new List<BacktestTrade>();
        while (await tradesReader.ReadAsync(ct))
        {
            trades.Add(ReadTrade(tradesReader));
        }

        return result with { Trades = trades };
    }

    private static BacktestRunResult ReadRunHeader(NpgsqlDataReader reader)
    {
        var runId = reader.GetGuid(reader.GetOrdinal("run_id"));
        var signalType = reader.GetString(reader.GetOrdinal("signal_type"));
        var signalParams = reader.IsDBNull(reader.GetOrdinal("signal_parameters"))
            ? null : reader.GetString(reader.GetOrdinal("signal_parameters"));
        var rmeConfigJson = reader.IsDBNull(reader.GetOrdinal("rme_config_json"))
            ? null : reader.GetString(reader.GetOrdinal("rme_config_json"));

        // Parse equity curve from metrics_json
        var equityCurve = new List<EquityPoint>();
        if (!reader.IsDBNull(reader.GetOrdinal("metrics_json")))
        {
            var raw = reader.GetString(reader.GetOrdinal("metrics_json"));
            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("equity_curve", out var curveEl))
                {
                    foreach (var pt in curveEl.EnumerateArray())
                    {
                        equityCurve.Add(new EquityPoint
                        {
                            Date = DateOnly.Parse(pt.GetProperty("date").GetString()!),
                            Equity = pt.GetProperty("equity").GetDecimal()
                        });
                    }
                }
            }
            catch (JsonException)
            {
                // metrics_json parse failure — continue with empty equity curve
            }
        }

        return new BacktestRunResult
        {
            RunId = runId,
            SignalType = signalType,
            SignalSubscriptionVersionId = signalParams,
            Timeframe = reader.GetString(reader.GetOrdinal("timeframe")),
            Slippage = reader.GetDecimal(reader.GetOrdinal("slippage")),
            CommissionPct = reader.GetDecimal(reader.GetOrdinal("commission_pct")),
            DateRangeStart = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("date_range_start"))),
            DateRangeEnd = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("date_range_end"))),
            RunTimestamp = reader.GetDateTime(reader.GetOrdinal("run_timestamp")),
            StartingEquity = reader.IsDBNull(reader.GetOrdinal("starting_equity")) ? 1_000_000m : reader.GetDecimal(reader.GetOrdinal("starting_equity")),
            EndingEquity = reader.IsDBNull(reader.GetOrdinal("ending_equity")) ? 0 : reader.GetDecimal(reader.GetOrdinal("ending_equity")),
            TotalTrades = reader.IsDBNull(reader.GetOrdinal("total_trades")) ? 0 : reader.GetInt32(reader.GetOrdinal("total_trades")),
            WinningTrades = reader.IsDBNull(reader.GetOrdinal("winning_trades")) ? 0 : reader.GetInt32(reader.GetOrdinal("winning_trades")),
            LosingTrades = reader.IsDBNull(reader.GetOrdinal("losing_trades")) ? 0 : reader.GetInt32(reader.GetOrdinal("losing_trades")),
            WinRatePct = reader.IsDBNull(reader.GetOrdinal("total_trades")) || reader.GetInt32(reader.GetOrdinal("total_trades")) == 0
                ? 0 : (decimal)reader.GetInt32(reader.GetOrdinal("winning_trades")) / reader.GetInt32(reader.GetOrdinal("total_trades")) * 100m,
            AverageR = reader.IsDBNull(reader.GetOrdinal("average_r")) ? 0 : reader.GetDecimal(reader.GetOrdinal("average_r")),
            ExpectancyInR = reader.IsDBNull(reader.GetOrdinal("raw_expectancy")) ? 0 : reader.GetDecimal(reader.GetOrdinal("raw_expectancy")),
            AdjustedExpectancy = reader.IsDBNull(reader.GetOrdinal("adjusted_expectancy")) ? null : reader.GetDecimal(reader.GetOrdinal("adjusted_expectancy")),
            ProfitFactor = 0, // computed from trades
            MaxDrawdownPct = reader.IsDBNull(reader.GetOrdinal("ending_equity")) ? 0 : 0, // computed from equity curve
            MaxDrawdownInR = reader.IsDBNull(reader.GetOrdinal("max_drawdown_in_r")) ? 0 : reader.GetDecimal(reader.GetOrdinal("max_drawdown_in_r")),
            SharpeRatio = 0, // stored in metrics_json or computed
            SortinoRatio = reader.IsDBNull(reader.GetOrdinal("sortino_ratio")) ? 0 : reader.GetDecimal(reader.GetOrdinal("sortino_ratio")),
            RecoveryFactor = reader.IsDBNull(reader.GetOrdinal("recovery_factor")) ? 0 : reader.GetDecimal(reader.GetOrdinal("recovery_factor")),
            TotalReturnPct = reader.IsDBNull(reader.GetOrdinal("total_return_pct")) ? 0 : reader.GetDecimal(reader.GetOrdinal("total_return_pct")),
            TotalReturnAbsolute = reader.IsDBNull(reader.GetOrdinal("total_return_absolute")) ? 0 : reader.GetDecimal(reader.GetOrdinal("total_return_absolute")),
            UniverseCoveragePct = reader.IsDBNull(reader.GetOrdinal("universe_coverage_pct")) ? 0 : reader.GetDecimal(reader.GetOrdinal("universe_coverage_pct")),
            LowConfidence = !reader.IsDBNull(reader.GetOrdinal("low_confidence")) && reader.GetBoolean(reader.GetOrdinal("low_confidence")),
            SkippedSessions = reader.IsDBNull(reader.GetOrdinal("skipped_sessions")) ? 0 : reader.GetInt32(reader.GetOrdinal("skipped_sessions")),
            TotalSessions = reader.IsDBNull(reader.GetOrdinal("total_sessions")) ? 0 : reader.GetInt32(reader.GetOrdinal("total_sessions")),
            RawExpectancy = reader.IsDBNull(reader.GetOrdinal("raw_expectancy")) ? 0 : reader.GetDecimal(reader.GetOrdinal("raw_expectancy")),
            SurvivorshipBiasDiscountPct = reader.IsDBNull(reader.GetOrdinal("survivorship_bias_discount_pct")) ? 0 : reader.GetDecimal(reader.GetOrdinal("survivorship_bias_discount_pct")),
            RmeConfigurationJson = rmeConfigJson,
            Trades = [], // populated separately
            PositionEvents = [],
            EquityCurve = equityCurve,
        };
    }

    private static BacktestTrade ReadTrade(NpgsqlDataReader reader)
    {
        return new BacktestTrade
        {
            Symbol = reader.GetString(reader.GetOrdinal("symbol")),
            EntryDate = DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("entry_date"))),
            ExitDate = reader.IsDBNull(reader.GetOrdinal("exit_date"))
                ? null : DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("exit_date"))),
            EntryPrice = reader.GetDecimal(reader.GetOrdinal("entry_price")),
            ExitPrice = reader.IsDBNull(reader.GetOrdinal("exit_price"))
                ? null : reader.GetDecimal(reader.GetOrdinal("exit_price")),
            StopAtEntry = reader.GetDecimal(reader.GetOrdinal("stop_at_entry")),
            ExitReason = reader.IsDBNull(reader.GetOrdinal("exit_reason"))
                ? null : reader.GetString(reader.GetOrdinal("exit_reason")),
            RMultiple = reader.IsDBNull(reader.GetOrdinal("r_multiple")) ? 0 : reader.GetDecimal(reader.GetOrdinal("r_multiple")),
            GrossPnL = reader.GetDecimal(reader.GetOrdinal("gross_pnl")),
            NetPnL = reader.GetDecimal(reader.GetOrdinal("net_pnl")),
            Quantity = 1,
        };
    }

    public async Task<List<BacktestRunHeader>> ListRunsAsync(
        int limit = 20, int offset = 0, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT rh.run_id, rh.signal_type, rh.timeframe,
                   rh.date_range_start, rh.date_range_end,
                   rh.run_timestamp,
                   pp.starting_equity, pp.ending_equity,
                   pp.win_rate_pct, pp.expectancy,
                   (SELECT COUNT(*) FROM BT_Trades t WHERE t.run_id = rh.run_id) AS total_trades
            FROM BT_RunHeaders rh
            LEFT JOIN BT_PortfolioPerformance pp ON pp.run_id = rh.run_id
            ORDER BY rh.run_timestamp DESC
            LIMIT @Limit OFFSET @Offset";

        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new NpgsqlCommand(sql, conn);
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
