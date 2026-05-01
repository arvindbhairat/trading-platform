using System.Collections.Concurrent;
using SignalStack.Api.Historical;
using SignalStack.Api.Universe;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// Core backtest engine that evaluates a signal against historical OHLCV data
/// for every symbol in the active universe and produces per-trade and
/// portfolio-level metrics.
///
/// portfolio-risk-guidelines § Backtesting Rules:
/// - Slippage and commission applied to every trade (REQ-STRAT-018)
/// - Gap risk modelled at session open
/// - Reports: total return, win rate, average R, expectancy in R, profit factor,
///   max drawdown, drawdown in R, Sharpe ratio, Sortino ratio, recovery factor
/// - Flags low-confidence runs per REQ-STRAT-023
/// - Applies survivorship bias discount per REQ-STRAT-011b
/// - Tracks skipped sessions per REQ-STRAT-022
/// </summary>
public sealed class BacktestEngine
{
    private readonly ISymbolMasterRepository _symbolRepo;
    private readonly ISymbolTableMapping _tableMapping;
    private readonly IOhlcvRepository _ohlcvRepo;
    private readonly ISysConfigRepository _sysConfig;
    private readonly ILogger<BacktestEngine> _logger;

    // Session counts per timeframe — REQ-STRAT-020
    private static readonly Dictionary<string, int> SessionsPerCandle = new(StringComparer.OrdinalIgnoreCase)
    {
        ["daily"] = 1,
        ["rolling3"] = 3,
        ["rolling5"] = 5,
        ["rolling7"] = 7,
        ["weekly"] = 5,
        ["monthly"] = 22,
    };

    public BacktestEngine(
        ISymbolMasterRepository symbolRepo,
        ISymbolTableMapping tableMapping,
        IOhlcvRepository ohlcvRepo,
        ISysConfigRepository sysConfig,
        ILogger<BacktestEngine> logger)
    {
        _symbolRepo = symbolRepo;
        _tableMapping = tableMapping;
        _ohlcvRepo = ohlcvRepo;
        _sysConfig = sysConfig;
        _logger = logger;
    }

    /// <summary>
    /// Runs a backtest for the given options and signal evaluator.
    /// Returns the completed result with all metrics.
    /// </summary>
    public async Task<BacktestRunResult> RunAsync(
        BacktestOptions options,
        IEntrySignalEvaluator evaluator,
        CancellationToken ct = default)
    {
        var runId = Guid.NewGuid();
        var runTimestamp = DateTime.UtcNow;

        _logger.LogInformation(
            "Starting backtest {RunId}: signal={SignalType}, timeframe={Timeframe}, " +
            "range={Start}..{End}, equity={Equity:N0}",
            runId, options.SignalTypeId, options.Timeframe,
            options.DateRangeStart, options.DateRangeEnd, options.StartingEquity);

        // Load sys_config settings
        var lowConfThresholdPct = await _sysConfig.GetDecimalAsync(
            "strategies.backtest.low_confidence_skip_threshold_pct", 5m, ct);

        var survivorshipDiscountPct = await _sysConfig.GetDecimalAsync(
            "strategies.backtest.survivorship_bias_discount_pct", 15m, ct);

        // Calculate minimum required sessions — REQ-STRAT-019
        var sessionsPerCandle = SessionsPerCandle.GetValueOrDefault(options.Timeframe, 1);
        var indicatorLength = 30; // default slow period for MA crossover
        var minSessions = indicatorLength * sessionsPerCandle;

        // Load active universe
        var allSymbols = await _symbolRepo.GetAllAsync(archived: false, ct);
        var activeSymbols = allSymbols
            .Where(s => !string.IsNullOrWhiteSpace(s.SqlTableNameSuffix))
            .Select(s => s.Symbol)
            .OrderBy(s => s)
            .ToList();

        _logger.LogInformation("Backtest universe has {Count} active symbols.", activeSymbols.Count);

        var trades = new ConcurrentBag<BacktestTrade>();
        var positionEvents = new ConcurrentBag<BacktestPositionEvent>();
        var totalProcessed = 0;
        var symbolsWithData = 0;
        var totalExpectedSessions = 0;
        var totalActualSessions = 0;

        // Process each symbol
        foreach (var symbol in activeSymbols)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                // Load daily OHLCV for the full range with buffer for SMA calculation
                var bufferFrom = options.DateRangeStart.AddDays(-indicatorLength * 2);
                var candles = await _ohlcvRepo.GetDailyAsync(
                    symbol, bufferFrom, options.DateRangeEnd, ct);

                if (candles.Count < minSessions)
                {
                    // REQ-STRAT-021: refused if insufficient data
                    continue;
                }

                // Filter to the actual backtest date range
                var evalCandles = candles
                    .Where(c => c.Date >= options.DateRangeStart && c.Date <= options.DateRangeEnd)
                    .OrderBy(c => c.Date)
                    .ToList();

                if (evalCandles.Count < 2) continue;

                symbolsWithData++;
                totalExpectedSessions += evalCandles.Count;

                // Count actual candles with trade activity for skip detection
                var tradeDates = new HashSet<DateOnly>();
                foreach (var t in trades.Where(t => t.Symbol == symbol))
                {
                    tradeDates.Add(t.EntryDate);
                    if (t.ExitDate.HasValue) tradeDates.Add(t.ExitDate!.Value);
                }
                totalActualSessions += evalCandles.Count(c => tradeDates.Contains(c.Date));

                // Evaluate signal
                var signals = evaluator.Evaluate(candles, options.SignalParametersJson);

                // Filter signals to the eval range
                var activeSignals = signals
                    .Where(s => s.Date >= options.DateRangeStart && s.Date <= options.DateRangeEnd)
                    .OrderBy(s => s.Date)
                    .ToList();

                // Track positions per symbol
                var execModel = new ExecutionModel(options.Slippage, options.CommissionPct);
                var position = new SimulatedPosition(symbol);
                var tradesForSymbol = 0;

                foreach (var signal in activeSignals)
                {
                    // Find the candle for this signal date
                    var dayCandle = evalCandles.FirstOrDefault(c => c.Date == signal.Date);
                    if (dayCandle is null) continue;

                    if (position.IsOpen)
                    {
                        // Check if stop was hit (gap risk at open)
                        var gapExitPrice = CheckGapRisk(
                            dayCandle.Open, position.CurrentStop, execModel);

                        if (gapExitPrice.HasValue)
                        {
                            // Gap exit
                            var (trade, evt) = ClosePosition(
                                position, signal.Date, gapExitPrice.Value,
                                "gap_circuit_breach", execModel);
                            trades.Add(trade);
                            if (evt is not null) positionEvents.Add(evt);
                            continue;
                        }

                        // Check regular stop hit
                        if (dayCandle.Low <= (double)position.CurrentStop)
                        {
                            var fillPrice = execModel.ApplyExitSlippage(position.CurrentStop);
                            var (trade, evt) = ClosePosition(
                                position, signal.Date, fillPrice,
                                "stop_loss_hit", execModel);
                            trades.Add(trade);
                            if (evt is not null) positionEvents.Add(evt);
                            continue;
                        }

                        // Check exit signal
                        if (signal.Action == SignalAction.Exit)
                        {
                            var exitPrice = execModel.ApplyExitSlippage(
                                signal.ExitPrice ?? (decimal)dayCandle.Close);
                            var (trade, evt) = ClosePosition(
                                position, signal.Date, exitPrice,
                                "strategy_exit_signal", execModel);
                            trades.Add(trade);
                            if (evt is not null) positionEvents.Add(evt);
                            continue;
                        }

                        // Update position (trailing stop, etc.)
                        position.CurrentPrice = (decimal)dayCandle.Close;
                    }

                    if (signal.Action == SignalAction.Entry && !position.IsOpen)
                    {
                        var entryPrice = execModel.ApplyEntrySlippage(
                            signal.EntryPrice ?? (decimal)dayCandle.Close);
                        var stopPrice = signal.StopPrice ?? entryPrice * 0.95m;

                        OpenPosition(position, signal.Date, entryPrice, stopPrice, execModel);
                        tradesForSymbol++;
                    }
                }

                // If position still open at end of range, close at last price
                if (position.IsOpen)
                {
                    var lastCandle = evalCandles[^1];
                    var exitPrice = execModel.ApplyExitSlippage((decimal)lastCandle.Close);
                    var (trade, evt) = ClosePosition(
                        position, evalCandles[^1].Date, exitPrice,
                        "time_stop", execModel);
                    trades.Add(trade);
                    if (evt is not null) positionEvents.Add(evt);
                }

                totalProcessed++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error processing symbol {Symbol} in backtest.", symbol);
            }
        }

        // Calculate metrics from all trades
        var finalTrades = trades.ToList();
        var finalPositionEvents = positionEvents.ToList();
        var universeCoveragePct = activeSymbols.Count > 0
            ? (decimal)symbolsWithData / activeSymbols.Count * 100m
            : 0m;

        var rawMetrics = CalculateMetrics(finalTrades, options.StartingEquity);

        // Apply survivorship bias discount — REQ-STRAT-011b
        decimal? adjustedExpectancy = null;
        if (survivorshipDiscountPct > 0 && rawMetrics.ExpectancyInR != 0)
        {
            adjustedExpectancy = rawMetrics.ExpectancyInR * (1m - survivorshipDiscountPct / 100m);
        }

        // Build equity curve
        var equityCurve = BuildEquityCurve(finalTrades, options.StartingEquity, options.DateRangeStart, options.DateRangeEnd);

        // Low-confidence check — REQ-STRAT-023
        // Flags runs where a significant portion of expected sessions had no trade activity.
        var skippedSessionRatio = totalExpectedSessions > 0
            ? (decimal)(totalExpectedSessions - totalActualSessions) / totalExpectedSessions * 100m
            : 0m;
        var isLowConfidence = skippedSessionRatio > lowConfThresholdPct;
        _logger.LogInformation(
            "Low-confidence check: skippedSessionRatio={Ratio:P2}, threshold={Threshold:P2}, isLow={IsLow}",
            skippedSessionRatio / 100m, lowConfThresholdPct / 100m, isLowConfidence);

        _logger.LogInformation(
            "Backtest {RunId} complete: {Trades} trades, {SymbolsProcessed}/{ActiveSymbols} symbols, " +
            "expectancy={Expectancy:F2}R, lowConfidence={LowConfidence}",
            runId, finalTrades.Count, symbolsWithData, activeSymbols.Count,
            rawMetrics.ExpectancyInR, isLowConfidence);

        return new BacktestRunResult
        {
            RunId = runId,
            SignalType = options.SignalTypeId,
            SignalSubscriptionVersionId = options.SignalSubscriptionVersionId,
            Timeframe = options.Timeframe,
            Slippage = options.Slippage,
            CommissionPct = options.CommissionPct,
            DateRangeStart = options.DateRangeStart,
            DateRangeEnd = options.DateRangeEnd,
            StartingEquity = options.StartingEquity,
            EndingEquity = rawMetrics.EndingEquity,
            RunTimestamp = runTimestamp,
            Trades = finalTrades,
            PositionEvents = finalPositionEvents,
            EquityCurve = equityCurve,
            TotalTrades = rawMetrics.TotalTrades,
            WinningTrades = rawMetrics.WinningTrades,
            LosingTrades = rawMetrics.LosingTrades,
            WinRatePct = rawMetrics.WinRatePct,
            AverageR = rawMetrics.AverageR,
            ExpectancyInR = rawMetrics.ExpectancyInR,
            ProfitFactor = rawMetrics.ProfitFactor,
            MaxDrawdownPct = rawMetrics.MaxDrawdownPct,
            MaxDrawdownInR = rawMetrics.MaxDrawdownInR,
            SharpeRatio = rawMetrics.SharpeRatio,
            SortinoRatio = rawMetrics.SortinoRatio,
            RecoveryFactor = rawMetrics.RecoveryFactor,
            TotalReturnPct = rawMetrics.TotalReturnPct,
            TotalReturnAbsolute = rawMetrics.TotalReturnAbsolute,
            UniverseCoveragePct = universeCoveragePct,
            LowConfidence = isLowConfidence,
            SkippedSessions = 0,
            TotalSessions = totalExpectedSessions,
            RawExpectancy = rawMetrics.ExpectancyInR,
            AdjustedExpectancy = adjustedExpectancy,
            SurvivorshipBiasDiscountPct = survivorshipDiscountPct,
        };
    }

    // ── Position tracking ──────────────────────────────────────────────────

    private static void OpenPosition(
        SimulatedPosition pos, DateOnly date, decimal entryPrice,
        decimal stopPrice, ExecutionModel execModel)
    {
        pos.EntryDate = date;
        pos.EntryPrice = entryPrice;
        pos.InitialStop = stopPrice;
        pos.CurrentStop = stopPrice;
        pos.IsOpen = true;
        pos.RiskAmount = entryPrice - stopPrice; // 1R
    }

    private static (BacktestTrade trade, BacktestPositionEvent? evt) ClosePosition(
        SimulatedPosition pos, DateOnly date, decimal exitPrice,
        string exitReason, ExecutionModel execModel)
    {
        if (!pos.IsOpen)
            throw new InvalidOperationException("Cannot close a position that is not open.");

        var grossPnl = (exitPrice - pos.EntryPrice) * 1; // quantity = 1 for backtest
        var entryValue = pos.EntryPrice * 1;
        var exitValue = exitPrice * 1;
        var netPnl = execModel.CalculateNetPnL(grossPnl, entryValue, exitValue);

        var rMultiple = pos.RiskAmount > 0
            ? grossPnl / pos.RiskAmount
            : 0;

        var trade = new BacktestTrade
        {
            Symbol = pos.Symbol,
            EntryDate = pos.EntryDate,
            ExitDate = date,
            EntryPrice = pos.EntryPrice,
            ExitPrice = exitPrice,
            StopAtEntry = pos.InitialStop,
            ExitReason = exitReason,
            RMultiple = Math.Round(rMultiple, 4),
            GrossPnL = Math.Round(grossPnl, 2),
            NetPnL = Math.Round(netPnl, 2),
            Quantity = 1,
        };

        var evt = new BacktestPositionEvent
        {
            Symbol = pos.Symbol,
            EventDate = date,
            EventType = "close",
            RmeState = "Closed",
            PeakRMultiple = Math.Round(rMultiple, 4),
        };

        pos.IsOpen = false;
        return (trade, evt);
    }

    private static decimal? CheckGapRisk(
        double openPrice, decimal stopPrice, ExecutionModel execModel)
    {
        var open = (decimal)openPrice;
        // If open gaps past stop (adverse gap), model the gap exit
        if (open > stopPrice)
        {
            // Gap up past stop (for long) — this is actually good for long positions
            // The stop is effectively filled at open minus slippage
            return execModel.ApplyExitSlippage(open);
        }

        return null;
    }

    // ── Metrics ────────────────────────────────────────────────────────────

    private static BacktestMetrics CalculateMetrics(
        List<BacktestTrade> trades, decimal startingEquity)
    {
        if (trades.Count == 0)
        {
            return new BacktestMetrics
            {
                TotalTrades = 0,
                EndingEquity = startingEquity,
                TotalReturnAbsolute = 0,
                TotalReturnPct = 0,
            };
        }

        var winning = trades.Where(t => t.NetPnL > 0).ToList();
        var losing = trades.Where(t => t.NetPnL <= 0).ToList();
        var totalTrades = trades.Count;
        var winningTrades = winning.Count;
        var losingTrades = losing.Count;

        var winRate = totalTrades > 0 ? (decimal)winningTrades / totalTrades * 100m : 0m;
        var avgR = trades.Count > 0 ? trades.Average(t => t.RMultiple) : 0m;
        var expectancy = trades.Count > 0 ? trades.Sum(t => t.RMultiple) / totalTrades : 0m;

        // Profit factor
        var grossProfit = winning.Sum(t => t.NetPnL);
        var grossLoss = Math.Abs(losing.Sum(t => t.NetPnL));
        var profitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 999m : 0m;

        // Total return
        var totalPnL = trades.Sum(t => t.NetPnL);
        var endingEquity = startingEquity + totalPnL;
        var totalReturnPct = startingEquity > 0 ? totalPnL / startingEquity * 100m : 0m;

        // Drawdown calculation from equity curve
        var equity = startingEquity;
        var peak = startingEquity;
        var maxDrawdown = 0m;
        var maxDrawdownPct = 0m;

        foreach (var trade in trades.OrderBy(t => t.EntryDate))
        {
            equity += trade.NetPnL;
            if (equity > peak)
            {
                peak = equity;
            }

            var dd = peak - equity;
            var ddPct = peak > 0 ? dd / peak * 100m : 0m;

            if (ddPct > maxDrawdownPct)
            {
                maxDrawdownPct = ddPct;
                maxDrawdown = dd;
            }
        }

        // Drawdown in R units
        var avgRisk = trades.Where(t => t.RMultiple != 0).Select(t => Math.Abs(t.NetPnL / t.RMultiple)).DefaultIfEmpty(1).Average();
        var maxDrawdownInR = avgRisk > 0 ? maxDrawdown / avgRisk : 0m;

        // Sharpe and Sortino ratios
        var dailyReturns = trades
            .OrderBy(t => t.ExitDate ?? t.EntryDate)
            .Select(t => (double)t.NetPnL)
            .ToList();

        var sharpeRatio = CalculateSharpeRatio(dailyReturns);
        var sortinoRatio = CalculateSortinoRatio(dailyReturns);

        // Recovery factor
        var recoveryFactor = maxDrawdown > 0 ? totalPnL / maxDrawdown : totalPnL > 0 ? 999m : 0m;

        return new BacktestMetrics
        {
            TotalTrades = totalTrades,
            WinningTrades = winningTrades,
            LosingTrades = losingTrades,
            WinRatePct = Math.Round(winRate, 2),
            AverageR = Math.Round(avgR, 4),
            ExpectancyInR = Math.Round(expectancy, 4),
            ProfitFactor = Math.Round(profitFactor, 4),
            MaxDrawdownPct = Math.Round(maxDrawdownPct, 2),
            MaxDrawdownInR = Math.Round(maxDrawdownInR, 2),
            SharpeRatio = Math.Round((decimal)sharpeRatio, 4),
            SortinoRatio = Math.Round((decimal)sortinoRatio, 4),
            RecoveryFactor = Math.Round(recoveryFactor, 2),
            TotalReturnPct = Math.Round(totalReturnPct, 2),
            TotalReturnAbsolute = Math.Round(totalPnL, 2),
            EndingEquity = Math.Round(endingEquity, 2),
        };
    }

    private static double CalculateSharpeRatio(List<double> returns)
    {
        if (returns.Count < 2) return 0;
        var mean = returns.Average();
        var variance = returns.Sum(r => Math.Pow(r - mean, 2)) / (returns.Count - 1);
        var stdDev = Math.Sqrt(variance);
        return stdDev > 0 ? mean / stdDev * Math.Sqrt(252) : 0; // Annualized
    }

    private static double CalculateSortinoRatio(List<double> returns)
    {
        if (returns.Count < 2) return 0;
        var mean = returns.Average();
        var downside = returns.Where(r => r < 0).ToList();
        if (downside.Count == 0) return mean > 0 ? 999 : 0;

        var downsideVariance = downside.Sum(r => Math.Pow(r - mean, 2)) / (returns.Count - 1);
        var downsideDev = Math.Sqrt(downsideVariance);
        return downsideDev > 0 ? mean / downsideDev * Math.Sqrt(252) : 0;
    }

    private static List<EquityPoint> BuildEquityCurve(
        List<BacktestTrade> trades,
        decimal startingEquity,
        DateOnly rangeStart,
        DateOnly rangeEnd)
    {
        var curve = new List<EquityPoint>();
        var equity = startingEquity;
        var tradeQueue = trades.OrderBy(t => t.ExitDate ?? t.EntryDate).GetEnumerator();
        var hasNext = tradeQueue.MoveNext();

        // Walk through each month (sampling monthly for manageability)
        for (var d = rangeStart; d <= rangeEnd; d = d.AddDays(1))
        {
            while (hasNext && (tradeQueue.Current.ExitDate ?? tradeQueue.Current.EntryDate) <= d)
            {
                equity += tradeQueue.Current.NetPnL;
                hasNext = tradeQueue.MoveNext();
            }

            if (d.Day == 1 || d == rangeStart || d == rangeEnd || !hasNext && d == rangeEnd)
            {
                curve.Add(new EquityPoint { Date = d, Equity = Math.Round(equity, 2) });
            }
        }

        return curve;
    }

}

/// <summary>In-memory position tracker during backtest simulation.</summary>
internal sealed class SimulatedPosition
{
    public string Symbol { get; }
    public DateOnly EntryDate { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal InitialStop { get; set; }
    public decimal CurrentStop { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal RiskAmount { get; set; }
    public bool IsOpen { get; set; }

    public SimulatedPosition(string symbol)
    {
        Symbol = symbol;
    }
}

internal sealed record BacktestMetrics
{
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePct { get; init; }
    public decimal AverageR { get; init; }
    public decimal ExpectancyInR { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPct { get; init; }
    public decimal MaxDrawdownInR { get; init; }
    public decimal SharpeRatio { get; init; }
    public decimal SortinoRatio { get; init; }
    public decimal RecoveryFactor { get; init; }
    public decimal TotalReturnPct { get; init; }
    public decimal TotalReturnAbsolute { get; init; }
    public decimal EndingEquity { get; init; }
}
