using System.Collections.Concurrent;
using MongoDB.Bson;
using SignalStack.Api.Historical;
using SignalStack.Api.Signals;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Universe;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// RME profile optimisation engine. Runs a symbol's historical entry signals
/// through multiple RME profile configurations, computes Composite Scores
/// (REQ-RME-020a) and Robustness Factors (REQ-RME-020b), and returns
/// ranked results (REQ-RME-020).
///
/// REQ-RME-020: triggered manually by user from Signal configuration screen.
/// Multi-timeframe evaluation (REQ-RME-020c) is handled by adding each
/// timeframe as a grid dimension.
/// U-9: optimisation runs share the REQ-SLO-007 concurrency cap.
/// </summary>
public sealed class ProfileOptimisationEngine
{
    private readonly BacktestEngine _backtestEngine;
    private readonly IOptimisationResultRepository _resultRepo;
    private readonly ISignalSubscriptionRepository _subRepo;
    private readonly ISymbolMasterRepository _symbolRepo;
    private readonly ISysConfigRepository _sysConfig;
    private readonly IOhlcvRepository _ohlcvRepo;
    private readonly ILogger<ProfileOptimisationEngine> _logger;

    private static readonly Dictionary<string, IEntrySignalEvaluator> Evaluators = new()
    {
        ["ma_crossover"] = new MaCrossoverEvaluator(),
    };

    public ProfileOptimisationEngine(
        BacktestEngine backtestEngine,
        IOptimisationResultRepository resultRepo,
        ISignalSubscriptionRepository subRepo,
        ISymbolMasterRepository symbolRepo,
        ISysConfigRepository sysConfig,
        IOhlcvRepository ohlcvRepo,
        ILogger<ProfileOptimisationEngine> logger)
    {
        _backtestEngine = backtestEngine;
        _resultRepo = resultRepo;
        _subRepo = subRepo;
        _symbolRepo = symbolRepo;
        _sysConfig = sysConfig;
        _ohlcvRepo = ohlcvRepo;
        _logger = logger;
    }

    /// <summary>
    /// Runs a full optimisation for the given subscription + symbol across
    /// all profile configurations in the developer-defined grid.
    /// </summary>
    public async Task<OptimisationRunSummary> RunOptimisationAsync(
        ObjectId subscriptionId,
        string symbol,
        string userId,
        DateOnly dateRangeStart,
        DateOnly dateRangeEnd,
        ProfileOptimisationGrid? grid = null,
        CancellationToken ct = default)
    {
        // Prevent duplicate active runs — REQ-RME-020
        var active = await _resultRepo.GetActiveRunAsync(subscriptionId, symbol, ct);
        if (active is not null)
        {
            throw new InvalidOperationException(
                "An optimisation run is already in progress for this subscription and symbol. " +
                "Please wait for it to complete before triggering another.");
        }

        // Load subscription
        var sub = await _subRepo.GetByIdAndUserAsync(subscriptionId, userId, ct);
        if (sub is null)
            throw new InvalidOperationException("Signal Subscription not found.");

        if (sub.Status == SubscriptionStatus.Paused)
            throw new InvalidOperationException("Cannot run optimisation on a paused subscription.");

        if (!Evaluators.TryGetValue(sub.SignalTypeId, out var evaluator))
            throw new InvalidOperationException(
                $"Unknown signal type '{sub.SignalTypeId}'. " +
                $"Available: {string.Join(", ", Evaluators.Keys)}");

        // Load sys_config thresholds
        var minTrades = (int)await _sysConfig.GetLongAsync(
            "rme.optimisation.min_trades_for_valid_result", 10, ct);
        var drawdownPenaltyThresholdPct = await _sysConfig.GetDecimalAsync(
            "rme.optimisation.drawdown_penalty_threshold_pct", 25m, ct);
        var walkForwardFolds = (int)await _sysConfig.GetLongAsync(
            "rme.optimisation.walk_forward_folds", 4, ct);

        // Resolve live signal subscription version ID
        var liveVersion = sub.CurrentVersionIndex >= 0 && sub.CurrentVersionIndex < sub.Versions.Count
            ? sub.Versions[sub.CurrentVersionIndex]
            : null;

        // Generate grid
        grid ??= new ProfileOptimisationGrid();
        var profiles = grid.GenerateAll();
        _logger.LogInformation(
            "Optimisation run starting: subscription={SubId}, symbol={Symbol}, " +
            "{ConfigCount} profile configs to evaluate",
            subscriptionId, symbol, profiles.Count);

        // Create the run document
        var runDoc = new OptimisationResultDocument
        {
            UserId = userId,
            SubscriptionId = subscriptionId,
            Symbol = symbol,
            TriggeredAt = DateTime.UtcNow,
            Status = "running",
            DateRangeStart = dateRangeStart,
            DateRangeEnd = dateRangeEnd,
            SignalSubscriptionVersionId = liveVersion?.VersionId.ToString() ?? "",
            SignalCodeVersions = new Dictionary<string, string>
            {
                ["signal_type"] = sub.SignalTypeId,
            },
            Results = [],
            GridSnapshotJson = System.Text.Json.JsonSerializer.Serialize(grid),
            EvaluatedSymbols = [symbol],
        };
        await _resultRepo.SaveAsync(runDoc, ct);

        try
        {
            // Run all profiles — collect raw results first
            var rawResults = new ConcurrentBag<ProfileConfigRawResult>();
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Min(profiles.Count, 4),
                CancellationToken = ct,
            };

            // Run each profile config
            var completedCount = 0;
            foreach (var profile in profiles)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    // Build BacktestOptions for this single-symbol, single-profile run
                    var options = new BacktestOptions
                    {
                        SignalTypeId = sub.SignalTypeId,
                        SignalSubscriptionVersionId = liveVersion?.VersionId.ToString(),
                        Timeframe = profile.Timeframe,
                        Slippage = 0.001m,
                        CommissionPct = 0.0001m,
                        DateRangeStart = dateRangeStart,
                        DateRangeEnd = dateRangeEnd,
                        StartingEquity = 1_000_000m,
                        RmeConfigurationJson = profile.ToJsonString(),
                        SignalParametersJson = sub.Parameters?.ToJson(),
                        Symbols = [symbol],
                    };

                    var result = await _backtestEngine.RunAsync(options, evaluator, ct);

                    // Compute annualised return (CAGR)
                    var totalDays = (dateRangeEnd.ToDateTime(TimeOnly.MinValue) -
                                     dateRangeStart.ToDateTime(TimeOnly.MinValue)).Days;
                    var years = Math.Max(1, totalDays) / 365.25m;
                    var cagr = years > 0 && result.StartingEquity > 0
                        ? (decimal)Math.Pow(
                            (double)(result.EndingEquity / result.StartingEquity),
                            1.0 / (double)years) - 1m
                        : 0m;

                    rawResults.Add(new ProfileConfigRawResult
                    {
                        Profile = profile,
                        TotalTrades = result.TotalTrades,
                        WinningTrades = result.WinningTrades,
                        LosingTrades = result.LosingTrades,
                        WinRatePct = result.WinRatePct,
                        AverageR = result.AverageR,
                        ExpectancyInR = result.ExpectancyInR,
                        ProfitFactor = result.ProfitFactor,
                        MaxDrawdownPct = result.MaxDrawdownPct,
                        SharpeRatio = result.SharpeRatio,
                        TotalReturnAnnualisedPct = cagr * 100m,
                        EquityCurve = result.EquityCurve,
                    });

                    Interlocked.Increment(ref completedCount);
                    _logger.LogDebug(
                        "Profile {Label}: {Trades} trades, expectancy={Exp:F4}R",
                        profile.Label, result.TotalTrades, result.ExpectancyInR);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Profile {Label} evaluation failed, skipping", profile.Label);
                }
            }

            var allRaw = rawResults.ToList();
            _logger.LogInformation(
                "Optimisation run {RunId}: {Completed}/{Total} profiles evaluated",
                runDoc.Id, completedCount, profiles.Count);

            // Compute Composite Scores (REQ-RME-020a)
            var scored = ComputeCompositeScores(allRaw, drawdownPenaltyThresholdPct, minTrades);

            // Compute Robustness Factors (REQ-RME-020b)
            scored = ComputeRobustnessFactors(scored, walkForwardFolds, dateRangeStart, dateRangeEnd);

            // Rank by final score descending
            scored = [.. scored.OrderByDescending(s => s.FinalScore)];

            // Build storage results
            var storedResults = scored.Select(r => new ProfileConfigResult
            {
                ProfileJson = r.Profile.ToJsonString(),
                ProfileLabel = r.Profile.Label,
                TotalTrades = r.TotalTrades,
                WinningTrades = r.WinningTrades,
                LosingTrades = r.LosingTrades,
                WinRatePct = r.WinRatePct,
                AverageR = r.AverageR,
                ExpectancyInR = r.ExpectancyInR,
                ProfitFactor = r.ProfitFactor,
                MaxDrawdownPct = r.MaxDrawdownPct,
                SharpeRatio = r.SharpeRatio,
                TotalReturnAnnualisedPct = r.TotalReturnAnnualisedPct,
                CompositeScore = r.CompositeScore,
                ComponentScores = r.ComponentScores,
                RobustnessFactor = r.RobustnessFactor,
                RobustnessSubScores = r.RobustnessSubScores,
                FinalScore = r.FinalScore,
                IsLowConfidence = r.IsLowConfidence,
            }).ToList();

            await _resultRepo.UpdateCompletionAsync(runDoc.Id, storedResults, DateTime.UtcNow, ct);

            return new OptimisationRunSummary
            {
                RunId = runDoc.Id,
                Symbol = symbol,
                Status = "completed",
                Results = scored,
                EvaluatedCount = scored.Count,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Optimisation run {RunId} failed", runDoc.Id);
            await _resultRepo.MarkFailedAsync(runDoc.Id, ct);
            throw;
        }
    }

    /// <summary>Computes Composite Scores for all raw results. REQ-RME-020a.</summary>
    private static List<ProfileConfigScored> ComputeCompositeScores(
        List<ProfileConfigRawResult> rawResults,
        decimal drawdownPenaltyThresholdPct,
        int minTrades)
    {
        if (rawResults.Count == 0) return [];

        // Min-max normalisation across all configs in the run
        var maxCagr = rawResults.Max(r => r.TotalReturnAnnualisedPct);
        var minCagr = rawResults.Min(r => r.TotalReturnAnnualisedPct);
        var maxSharpe = rawResults.Max(r => r.SharpeRatio);
        var minSharpe = rawResults.Min(r => r.SharpeRatio);
        var maxExpectancy = rawResults.Max(r => r.ExpectancyInR);
        var minExpectancy = rawResults.Min(r => r.ExpectancyInR);
        var maxProfitFactor = rawResults.Max(r => r.ProfitFactor);
        var minProfitFactor = rawResults.Min(r => r.ProfitFactor);

        var scored = new List<ProfileConfigScored>();

        foreach (var raw in rawResults)
        {
            var isLowConfidence = raw.TotalTrades < minTrades;

            // Normalise components to [0, 1]
            var normCagr = Normalise(raw.TotalReturnAnnualisedPct, minCagr, maxCagr);
            var normSharpe = Normalise(raw.SharpeRatio, minSharpe, maxSharpe);
            var normExpectancy = Normalise(raw.ExpectancyInR, minExpectancy, maxExpectancy);
            var normProfitFactor = Normalise(raw.ProfitFactor, minProfitFactor, maxProfitFactor);

            // Stability Score: 1 - (StdDev / Mean) of sub-window returns
            var stabilityScore = ComputeStabilityScore(raw.EquityCurve);

            // Regime Consistency Score: simplified — divide period into thirds and compare
            var regimeConsistencyScore = ComputeRegimeConsistencyScore(raw.EquityCurve);

            // Capital Efficiency: total return / average capital deployed
            var capitalEfficiency = ComputeCapitalEfficiency(raw.EquityCurve, raw.TotalReturnAnnualisedPct);

            // Trade Quality Score: equal-weighted composite of avg R, profit factor, and win/loss symmetry
            var tradeQualityScore = ComputeTradeQualityScore(raw);

            // Drawdown Penalty: MaxDD% / threshold, capped at 1.0
            var drawdownPenalty = Math.Min(raw.MaxDrawdownPct / drawdownPenaltyThresholdPct, 1.0m);

            var componentScores = new Dictionary<string, decimal>
            {
                ["normalised_cagr"] = Math.Round(normCagr, 6),
                ["normalised_sharpe"] = Math.Round(normSharpe, 6),
                ["normalised_expectancy"] = Math.Round(normExpectancy, 6),
                ["normalised_profit_factor"] = Math.Round(normProfitFactor, 6),
                ["stability_score"] = Math.Round(stabilityScore, 6),
                ["regime_consistency_score"] = Math.Round(regimeConsistencyScore, 6),
                ["capital_efficiency"] = Math.Round(capitalEfficiency, 6),
                ["trade_quality_score"] = Math.Round(tradeQualityScore, 6),
                ["drawdown_penalty"] = Math.Round(drawdownPenalty, 6),
            };

            var compositeScore =
                (0.20m * normCagr)
                + (0.15m * normSharpe)
                + (0.15m * normExpectancy)
                + (0.10m * normProfitFactor)
                + (0.10m * stabilityScore)
                + (0.10m * regimeConsistencyScore)
                + (0.10m * capitalEfficiency)
                + (0.10m * tradeQualityScore)
                - (0.10m * drawdownPenalty);

            scored.Add(new ProfileConfigScored
            {
                Profile = raw.Profile,
                TotalTrades = raw.TotalTrades,
                WinningTrades = raw.WinningTrades,
                LosingTrades = raw.LosingTrades,
                WinRatePct = raw.WinRatePct,
                AverageR = raw.AverageR,
                ExpectancyInR = raw.ExpectancyInR,
                ProfitFactor = raw.ProfitFactor,
                MaxDrawdownPct = raw.MaxDrawdownPct,
                SharpeRatio = raw.SharpeRatio,
                TotalReturnAnnualisedPct = raw.TotalReturnAnnualisedPct,
                CompositeScore = Math.Round(compositeScore, 6),
                ComponentScores = componentScores,
                IsLowConfidence = isLowConfidence,
                EquityCurve = raw.EquityCurve,
            });
        }

        return scored;
    }

    /// <summary>Computes Robustness Factors. REQ-RME-020b.</summary>
    private static List<ProfileConfigScored> ComputeRobustnessFactors(
        List<ProfileConfigScored> scored,
        int walkForwardFolds,
        DateOnly dateRangeStart,
        DateOnly dateRangeEnd)
    {
        if (scored.Count == 0) return [];

        // Parameter Sensitivity Score:
        // For each config, find neighbours that differ by one parameter and compute the variance.
        var paramSensitivityScores = ComputeParameterSensitivityScores(scored);

        // Cross-Symbol Consistency: With single-symbol runs, this defaults to 1.0
        // (no variation across symbols). Will be properly computed in multi-symbol runs.
        const decimal crossSymbolConsistency = 1.0m;

        // Walk-Forward Stability Score: computed from the correlation of early vs late period
        var walkForwardScores = ComputeWalkForwardStability(scored, walkForwardFolds);

        foreach (var s in scored)
        {
            var paramScore = paramSensitivityScores.GetValueOrDefault(s.Profile.Label, 1.0m);
            var wfScore = walkForwardScores.GetValueOrDefault(s.Profile.Label, 1.0m);

            var robustnessFactor = paramScore * crossSymbolConsistency * wfScore;

            s.RobustnessFactor = Math.Round(robustnessFactor, 6);
            s.RobustnessSubScores = new Dictionary<string, decimal>
            {
                ["parameter_sensitivity"] = Math.Round(paramScore, 6),
                ["cross_symbol_consistency"] = Math.Round(crossSymbolConsistency, 6),
                ["walk_forward_stability"] = Math.Round(wfScore, 6),
            };
            s.FinalScore = Math.Round(s.CompositeScore * robustnessFactor, 6);
        }

        return scored;
    }

    // ── Composite Score sub-computations ──────────────────────

    private static decimal Normalise(decimal value, decimal min, decimal max)
    {
        if (Math.Abs(max - min) < 0.0001m) return 0.5m; // neutral when no spread
        return Math.Clamp((value - min) / (max - min), 0m, 1m);
    }

    private static decimal ComputeStabilityScore(List<EquityPoint> equityCurve)
    {
        if (equityCurve.Count < 4) return 0.5m;

        // Divide into rolling sub-windows (4 equal windows)
        var windowSize = Math.Max(1, equityCurve.Count / 4);
        var windowReturns = new List<decimal>();
        for (int i = 0; i + windowSize <= equityCurve.Count; i += windowSize / 2)
        {
            var startEquity = equityCurve[i].Equity;
            var endIndex = Math.Min(i + windowSize, equityCurve.Count) - 1;
            var endEquity = equityCurve[endIndex].Equity;
            if (startEquity > 0)
                windowReturns.Add((endEquity - startEquity) / startEquity);
        }

        if (windowReturns.Count < 2) return 0.5m;

        var mean = windowReturns.Average();
        if (Math.Abs(mean) < 0.0001m) return 0.5m;

        var variance = windowReturns.Sum(r => (r - mean) * (r - mean)) / (windowReturns.Count - 1);
        var stdDev = (decimal)Math.Sqrt((double)variance);
        var stability = 1m - (stdDev / Math.Abs(mean));
        return Math.Clamp(stability, 0m, 1m);
    }

    private static decimal ComputeRegimeConsistencyScore(List<EquityPoint> equityCurve)
    {
        if (equityCurve.Count < 6) return 0.5m;

        // Simplified: divide into thirds
        var third = equityCurve.Count / 3;
        if (third < 2) return 0.5m;

        var segmentReturns = new List<decimal>();
        for (int i = 0; i < 3; i++)
        {
            var startIdx = i * third;
            var endIdx = Math.Min((i + 1) * third, equityCurve.Count) - 1;
            var startEq = equityCurve[startIdx].Equity;
            var endEq = equityCurve[endIdx].Equity;
            if (startEq > 0)
                segmentReturns.Add((endEq - startEq) / startEq);
        }

        if (segmentReturns.Count < 2) return 0.5m;

        // Consistency = 1 - CV of returns across regimes
        var mean = segmentReturns.Average();
        if (Math.Abs(mean) < 0.0001m) return 0.5m;

        var stdDev = (decimal)Math.Sqrt(
            (double)segmentReturns.Sum(r => (r - mean) * (r - mean)) / (segmentReturns.Count - 1));
        var cv = stdDev / Math.Abs(mean);

        // Lower CV → more consistent → higher score
        return Math.Clamp(1m - Math.Min(cv, 1m), 0m, 1m);
    }

    private static decimal ComputeCapitalEfficiency(
        List<EquityPoint> equityCurve, decimal totalReturnPct)
    {
        if (equityCurve.Count < 2) return 0.5m;

        var avgCapital = equityCurve.Average(e => e.Equity);
        // Total return relative to average capital
        // Scale: high efficiency → 1.0, low → 0.0
        // Return of 20% of avg capital → score ~0.8
        var efficiency = totalReturnPct / 100m; // ratio form
        return Math.Clamp(efficiency * 2m, 0m, 1m); // scale: 50% return → 1.0
    }

    private static decimal ComputeTradeQualityScore(ProfileConfigRawResult raw)
    {
        if (raw.TotalTrades < 1) return 0.5m;

        // Normalised average R (assume max meaningful R is 5)
        var avgRNorm = Math.Min(Math.Abs(raw.AverageR) / 5m, 1m);

        // Profit factor normalised (assume max meaningful PF is 5)
        var profitFactorNorm = Math.Min(raw.ProfitFactor / 5m, 1m);

        // Win/loss symmetry: how balanced are the wins and losses?
        var avgWinR = raw.WinningTrades > 0
            ? raw.TotalTrades > 0
                ? raw.AverageR * raw.TotalTrades / raw.WinningTrades * raw.WinRatePct / 100m
                : 0m
            : 0m;
        var avgLossR = raw.LosingTrades > 0
            ? raw.AverageR * raw.TotalTrades / raw.LosingTrades * (1m - raw.WinRatePct / 100m)
            : 0m;
        var symmetry = Math.Abs(avgLossR) > 0.0001m
            ? Math.Min(Math.Abs(avgWinR) / Math.Abs(avgLossR), 2m) / 2m
            : 1m;

        return Math.Clamp((avgRNorm + profitFactorNorm + symmetry) / 3m, 0m, 1m);
    }

    // ── Robustness Factor sub-computations ────────────────────

    private static Dictionary<string, decimal> ComputeParameterSensitivityScores(
        List<ProfileConfigScored> scored)
    {
        var result = new Dictionary<string, decimal>();

        foreach (var s in scored)
        {
            // Find neighbours: configs with same label except one parameter changes
            var neighbours = scored
                .Where(n => n.Profile.Label != s.Profile.Label
                            && n.Profile.SizingModelType == s.Profile.SizingModelType
                            && n.Profile.StopLossType == s.Profile.StopLossType
                            && n.Profile.Timeframe == s.Profile.Timeframe)
                .ToList();

            if (neighbours.Count < 2)
            {
                result[s.Profile.Label] = 1.0m; // No neighbours → no penalty
                continue;
            }

            // Compute variance of Composite Scores among neighbours
            var scores = neighbours.Select(n => (double)n.CompositeScore).ToList();
            scores.Add((double)s.CompositeScore); // Include self
            var mean = scores.Average();
            var variance = scores.Sum(x => (x - mean) * (x - mean)) / scores.Count;

            // Normalise: lower variance → higher score (closer to 1)
            // variance / (max possible variance + 1) as a rough normalisation
            var maxPossibleVariance = 0.25; // Rough upper bound for scores in [0, 1]
            var sensitivity = 1m - (decimal)Math.Min(variance / maxPossibleVariance, 1.0);

            result[s.Profile.Label] = Math.Clamp(sensitivity, 0m, 1m);
        }

        return result;
    }

    private static Dictionary<string, decimal> ComputeWalkForwardStability(
        List<ProfileConfigScored> scored, int folds)
    {
        var result = new Dictionary<string, decimal>();

        foreach (var s in scored)
        {
            if (s.EquityCurve.Count < folds * 2)
            {
                result[s.Profile.Label] = 1.0m;
                continue;
            }

            var foldSize = s.EquityCurve.Count / (folds + 1); // +1 for OOS
            if (foldSize < 1)
            {
                result[s.Profile.Label] = 1.0m;
                continue;
            }

            // Simplified: compare first half (in-sample) vs second half (out-of-sample)
            var midPoint = s.EquityCurve.Count / 2;
            var isReturn = midPoint > 0 && s.EquityCurve[0].Equity > 0
                ? (s.EquityCurve[midPoint - 1].Equity - s.EquityCurve[0].Equity) / s.EquityCurve[0].Equity
                : 0m;
            var oosReturn = midPoint < s.EquityCurve.Count && s.EquityCurve[midPoint].Equity > 0
                ? (s.EquityCurve[^1].Equity - s.EquityCurve[midPoint].Equity) / s.EquityCurve[midPoint].Equity
                : 0m;

            // If both in-sample and out-of-sample have the same sign, that is stable.
            // We compute a correlation-like score.
            var stability = (isReturn >= 0 && oosReturn >= 0) || (isReturn < 0 && oosReturn < 0)
                ? 1m - Math.Abs(isReturn - oosReturn) / (Math.Max(Math.Abs(isReturn), Math.Abs(oosReturn)) + 0.01m)
                : 0.3m; // Sign mismatch → penalised

            result[s.Profile.Label] = Math.Clamp(stability, 0m, 1m);
        }

        return result;
    }
}

// ── Internal data types ────────────────────────────────────

internal sealed record ProfileConfigRawResult
{
    public required RmeProfile Profile { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePct { get; init; }
    public decimal AverageR { get; init; }
    public decimal ExpectancyInR { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPct { get; init; }
    public decimal SharpeRatio { get; init; }
    public decimal TotalReturnAnnualisedPct { get; init; }
    public required List<EquityPoint> EquityCurve { get; init; }
}

public sealed class ProfileConfigScored
{
    public required RmeProfile Profile { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePct { get; init; }
    public decimal AverageR { get; init; }
    public decimal ExpectancyInR { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPct { get; init; }
    public decimal SharpeRatio { get; init; }
    public decimal TotalReturnAnnualisedPct { get; init; }
    public decimal CompositeScore { get; set; }
    public Dictionary<string, decimal> ComponentScores { get; set; } = [];
    public decimal RobustnessFactor { get; set; }
    public Dictionary<string, decimal> RobustnessSubScores { get; set; } = [];
    public decimal FinalScore { get; set; }
    public bool IsLowConfidence { get; init; }
    public List<EquityPoint> EquityCurve { get; init; } = [];
}

/// <summary>Public summary of an optimisation run, returned to the caller.</summary>
public sealed record OptimisationRunSummary
{
    public required ObjectId RunId { get; init; }
    public required string Symbol { get; init; }
    public required string Status { get; init; }
    public required List<ProfileConfigScored> Results { get; init; }
    public int EvaluatedCount { get; init; }
}
