using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using MongoDB.Bson;
using SignalStack.Api.Signals;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// API endpoints for launching backtests and retrieving results.
/// P4-T3: backtest engine + result storage + R metrics.
/// </summary>
public static class BacktestEndpoints
{
    private static readonly Dictionary<string, IEntrySignalEvaluator> Evaluators = new()
    {
        ["ma_crossover"] = new MaCrossoverEvaluator(),
    };

    public static RouteGroupBuilder MapBacktestEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/run", RunBacktestAsync);
        group.MapGet("/runs", ListRunsAsync);
        group.MapGet("/runs/{runId:guid}", GetRunAsync);
        group.MapPost("/run-from-subscription/{subscriptionId}", RunFromSubscriptionAsync);
        return group;
    }

    /// <summary>
    /// POST /api/v1/backtest/run
    /// Launches a backtest with the given parameters.
    /// Default starting equity from sys_config when not explicitly provided (REQ-BTSTORE-006a).
    /// </summary>
    public static async Task<Results<Ok<BacktestRunResponse>, BadRequest<string>, NotFound<string>>> RunBacktestAsync(
        RunBacktestRequest request,
        BacktestEngine engine,
        IBacktestRepository repository,
        ISysConfigRepository sysConfig,
        ILogger<BacktestEngine> logger,
        CancellationToken ct)
    {
        // Validate signal type
        if (!Evaluators.TryGetValue(request.SignalTypeId, out var evaluator))
        {
            return TypedResults.NotFound(
                $"Unknown signal type '{request.SignalTypeId}'. " +
                $"Available: {string.Join(", ", Evaluators.Keys)}");
        }

        // Validate date range
        if (request.DateRangeStart >= request.DateRangeEnd)
        {
            return TypedResults.BadRequest("DateRangeStart must be before DateRangeEnd.");
        }

        // Default starting equity from sys_config — REQ-BTSTORE-006a
        var startingEquity = request.StartingEquity;
        if (startingEquity <= 0)
        {
            startingEquity = await sysConfig.GetDecimalAsync(
                "strategies.backtest.default_starting_equity_inr", 1_000_000m, ct);
        }

        if (startingEquity <= 0)
        {
            return TypedResults.BadRequest("Starting equity must be greater than zero.");
        }

        if (request.Slippage < 0)
        {
            return TypedResults.BadRequest("Slippage must be non-negative.");
        }

        if (request.CommissionPct < 0)
        {
            return TypedResults.BadRequest("Commission must be non-negative.");
        }

        var options = new BacktestOptions
        {
            SignalTypeId = request.SignalTypeId,
            SignalSubscriptionVersionId = request.SignalSubscriptionVersionId,
            Timeframe = request.Timeframe,
            Slippage = request.Slippage,
            CommissionPct = request.CommissionPct,
            DateRangeStart = request.DateRangeStart,
            DateRangeEnd = request.DateRangeEnd,
            StartingEquity = startingEquity,
            RmeConfigurationJson = request.RmeConfigurationJson,
            SignalParametersJson = request.SignalParametersJson,
        };

        try
        {
            var result = await engine.RunAsync(options, evaluator, ct);

            // Persist results
            await repository.SaveRunHeaderAsync(result, ct);
            await repository.SaveTradesAsync(result.RunId, result.Trades, ct);
            await repository.SavePositionEventsAsync(result.RunId, result.PositionEvents, ct);
            await repository.SavePortfolioPerformanceAsync(result, ct);

            logger.LogInformation(
                "Backtest {RunId} saved. {Trades} trades, expectancy={Expectancy:F4}R, lowConfidence={Low}",
                result.RunId, result.TotalTrades, result.ExpectancyInR, result.LowConfidence);

            return TypedResults.Ok(MapToResponse(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backtest run failed for signal type {SignalType}.", request.SignalTypeId);
            return TypedResults.BadRequest($"Backtest failed: {ex.Message}");
        }
    }

    /// <summary>GET /api/v1/backtest/runs — list recent backtest runs.</summary>
    public static async Task<Ok<List<BacktestRunHeader>>> ListRunsAsync(
        IBacktestRepository repository,
        int limit = 20,
        int offset = 0,
        CancellationToken ct = default)
    {
        var headers = await repository.ListRunsAsync(limit, offset, ct);
        return TypedResults.Ok(headers);
    }

    /// <summary>GET /api/v1/backtest/runs/{runId} — get a single backtest run.</summary>
    public static async Task<Results<Ok<BacktestRunResponse>, NotFound<string>>> GetRunAsync(
        Guid runId,
        IBacktestRepository repository,
        CancellationToken ct)
    {
        var result = await repository.GetRunAsync(runId, ct);
        if (result is null)
            return TypedResults.NotFound($"Backtest run {runId} not found.");

        return TypedResults.Ok(MapToResponse(result));
    }

    /// <summary>
    /// POST /api/v1/backtest/run-from-subscription/{subscriptionId}
    /// Runs a backtest using an existing Signal Subscription's configuration.
    /// REQ-BTSTORE-003: version pinning — uses the subscription's current live RME version.
    /// REQ-BTSTORE-006a: default starting equity from sys_config.
    /// </summary>
    public static async Task<Results<Ok<BacktestRunResponse>, BadRequest<string>, NotFound<string>>> RunFromSubscriptionAsync(
        string subscriptionId,
        RunFromSubscriptionRequest request,
        BacktestEngine engine,
        IBacktestRepository repository,
        ISignalSubscriptionRepository subRepo,
        ISysConfigRepository sysConfig,
        HttpContext context,
        ILogger<BacktestEngine> logger,
        CancellationToken ct)
    {
        if (!ObjectId.TryParse(subscriptionId, out var oid))
            return TypedResults.BadRequest("Invalid subscription ID.");

        var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "";
        if (string.IsNullOrWhiteSpace(userId))
            return TypedResults.BadRequest("User not authenticated.");

        // Load subscription
        var sub = await subRepo.GetByIdAndUserAsync(oid, userId, ct);
        if (sub is null)
            return TypedResults.NotFound("Subscription not found.");

        if (sub.Status == SubscriptionStatus.Paused)
            return TypedResults.BadRequest("Cannot run backtest on a paused subscription.");

        // Validate evaluator
        if (!Evaluators.TryGetValue(sub.SignalTypeId, out var evaluator))
        {
            return TypedResults.NotFound(
                $"Unknown signal type '{sub.SignalTypeId}'. " +
                $"Available: {string.Join(", ", Evaluators.Keys)}");
        }

        // Validate date range
        if (request.DateRangeEnd <= request.DateRangeStart)
        {
            return TypedResults.BadRequest("DateRangeEnd must be after DateRangeStart.");
        }

        // Default starting equity from sys_config — REQ-BTSTORE-006a
        var startingEquity = request.StartingEquity > 0
            ? request.StartingEquity
            : await sysConfig.GetDecimalAsync(
                "strategies.backtest.default_starting_equity_inr", 1_000_000m, ct);

        // Resolve the RME config from the subscription's live version — REQ-BTSTORE-003 version pinning
        var liveVersion = sub.CurrentVersionIndex >= 0 && sub.CurrentVersionIndex < sub.Versions.Count
            ? sub.Versions[sub.CurrentVersionIndex]
            : null;

        var rmeConfigJson = liveVersion?.RmeConfiguration?.ToJson() ?? "{}";

        var options = new BacktestOptions
        {
            SignalTypeId = sub.SignalTypeId,
            SignalSubscriptionVersionId = liveVersion?.VersionId.ToString(),
            Timeframe = request.Timeframe ?? sub.Timeframe,
            Slippage = request.Slippage > 0 ? request.Slippage : 0.001m,
            CommissionPct = request.CommissionPct > 0 ? request.CommissionPct : 0.0001m,
            DateRangeStart = request.DateRangeStart,
            DateRangeEnd = request.DateRangeEnd,
            StartingEquity = startingEquity,
            RmeConfigurationJson = rmeConfigJson,
            SignalParametersJson = sub.Parameters?.ToJson(),
        };

        try
        {
            var result = await engine.RunAsync(options, evaluator, ct);

            // Persist results
            await repository.SaveRunHeaderAsync(result, ct);
            await repository.SaveTradesAsync(result.RunId, result.Trades, ct);
            await repository.SavePositionEventsAsync(result.RunId, result.PositionEvents, ct);
            await repository.SavePortfolioPerformanceAsync(result, ct);

            logger.LogInformation(
                "Backtest {RunId} from subscription {SubId}: {Trades} trades, expectancy={Expectancy:F4}R",
                result.RunId, subscriptionId, result.TotalTrades, result.ExpectancyInR);

            return TypedResults.Ok(MapToResponse(result));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backtest from subscription {SubId} failed.", subscriptionId);
            return TypedResults.BadRequest($"Backtest failed: {ex.Message}");
        }
    }

    private static BacktestRunResponse MapToResponse(BacktestRunResult result)
    {
        return new BacktestRunResponse
        {
            RunId = result.RunId,
            SignalType = result.SignalType,
            Timeframe = result.Timeframe,
            Slippage = result.Slippage,
            CommissionPct = result.CommissionPct,
            DateRangeStart = result.DateRangeStart,
            DateRangeEnd = result.DateRangeEnd,
            StartingEquity = result.StartingEquity,
            EndingEquity = result.EndingEquity,
            TotalTrades = result.TotalTrades,
            WinningTrades = result.WinningTrades,
            LosingTrades = result.LosingTrades,
            WinRatePct = result.WinRatePct,
            AverageR = result.AverageR,
            ExpectancyInR = result.ExpectancyInR,
            AdjustedExpectancy = result.AdjustedExpectancy,
            ProfitFactor = result.ProfitFactor,
            MaxDrawdownPct = result.MaxDrawdownPct,
            MaxDrawdownInR = result.MaxDrawdownInR,
            SharpeRatio = result.SharpeRatio,
            SortinoRatio = result.SortinoRatio,
            RecoveryFactor = result.RecoveryFactor,
            TotalReturnPct = result.TotalReturnPct,
            TotalReturnAbsolute = result.TotalReturnAbsolute,
            UniverseCoveragePct = result.UniverseCoveragePct,
            LowConfidence = result.LowConfidence,
            Trades = result.Trades.Select(t => new BacktestTradeResponse
            {
                Symbol = t.Symbol,
                EntryDate = t.EntryDate,
                ExitDate = t.ExitDate,
                EntryPrice = t.EntryPrice,
                ExitPrice = t.ExitPrice,
                StopAtEntry = t.StopAtEntry,
                ExitReason = t.ExitReason,
                RMultiple = t.RMultiple,
                GrossPnL = t.GrossPnL,
                NetPnL = t.NetPnL,
                Quantity = t.Quantity,
            }).ToList(),
            EquityCurve = result.EquityCurve.Select(e => new EquityPointResponse
            {
                Date = e.Date, Equity = e.Equity,
            }).ToList(),
        };
    }
}

// ── Request/Response DTOs ─────────────────────────────────────────────────

public sealed record RunBacktestRequest
{
    public required string SignalTypeId { get; init; }
    public string? SignalSubscriptionVersionId { get; init; }
    public string Timeframe { get; init; } = "daily";
    public decimal Slippage { get; init; } = 0.001m;
    public decimal CommissionPct { get; init; } = 0.0001m;
    public required DateOnly DateRangeStart { get; init; }
    public required DateOnly DateRangeEnd { get; init; }
    public decimal StartingEquity { get; init; } = 1_000_000m;
    public string? RmeConfigurationJson { get; init; }
    public string? SignalParametersJson { get; init; }
}

public sealed record BacktestRunResponse
{
    public required Guid RunId { get; init; }
    public required string SignalType { get; init; }
    public required string Timeframe { get; init; }
    public decimal Slippage { get; init; }
    public decimal CommissionPct { get; init; }
    public required DateOnly DateRangeStart { get; init; }
    public required DateOnly DateRangeEnd { get; init; }
    public decimal StartingEquity { get; init; }
    public decimal EndingEquity { get; init; }
    public int TotalTrades { get; init; }
    public int WinningTrades { get; init; }
    public int LosingTrades { get; init; }
    public decimal WinRatePct { get; init; }
    public decimal AverageR { get; init; }
    public decimal ExpectancyInR { get; init; }
    public decimal? AdjustedExpectancy { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPct { get; init; }
    public decimal MaxDrawdownInR { get; init; }
    public decimal SharpeRatio { get; init; }
    public decimal SortinoRatio { get; init; }
    public decimal RecoveryFactor { get; init; }
    public decimal TotalReturnPct { get; init; }
    public decimal TotalReturnAbsolute { get; init; }
    public decimal UniverseCoveragePct { get; init; }
    public bool LowConfidence { get; init; }
    public required List<BacktestTradeResponse> Trades { get; init; }
    public required List<EquityPointResponse> EquityCurve { get; init; }
}

public sealed record BacktestTradeResponse
{
    public required string Symbol { get; init; }
    public required DateOnly EntryDate { get; init; }
    public DateOnly? ExitDate { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal? ExitPrice { get; init; }
    public decimal StopAtEntry { get; init; }
    public string? ExitReason { get; init; }
    public decimal RMultiple { get; init; }
    public decimal GrossPnL { get; init; }
    public decimal NetPnL { get; init; }
    public int Quantity { get; init; }
}

public sealed record EquityPointResponse
{
    public required DateOnly Date { get; init; }
    public decimal Equity { get; init; }
}

public sealed record RunFromSubscriptionRequest
{
    public required DateOnly DateRangeStart { get; init; }
    public required DateOnly DateRangeEnd { get; init; }
    public string? Timeframe { get; init; }
    public decimal StartingEquity { get; init; }
    public decimal Slippage { get; init; }
    public decimal CommissionPct { get; init; }
}
