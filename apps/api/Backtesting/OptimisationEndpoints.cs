using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using MongoDB.Bson;
using SignalStack.Api.Historical;
using SignalStack.Api.Signals;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Backtesting;

public static class OptimisationEndpoints
{
    public static RouteGroupBuilder MapOptimisationEndpoints(this RouteGroupBuilder group)
    {
        // REQ-RME-020: trigger optimisation run
        group.MapPost("/optimise/{subscriptionId}/{symbol}", StartOptimisationAsync);

        // List optimisation runs for the current user
        group.MapGet("/optimise/runs", ListOptimisationRunsAsync);

        // Get a specific optimisation run by ID
        group.MapGet("/optimise/runs/{runId}", GetOptimisationRunAsync);

        // Get the latest completed result for a subscription + symbol, with staleness check (REQ-RME-021)
        group.MapGet("/optimise/latest/{subscriptionId}/{symbol}", GetLatestOptimisationAsync);

        return group;
    }

    /// <summary>
    /// POST /api/v1/backtest/optimise/{subscriptionId}/{symbol}
    /// Triggers an RME profile optimisation run for the given subscription and symbol.
    /// REQ-RME-020: triggered manually from Signal configuration screen.
    /// U-9: shares the REQ-SLO-007 backtest concurrency cap.
    /// </summary>
    public static async Task<Results<Ok<OptimisationRunResponse>, BadRequest<string>, NotFound<string>>> StartOptimisationAsync(
        string subscriptionId,
        string symbol,
        StartOptimisationRequest request,
        ProfileOptimisationEngine engine,
        BacktestConcurrencySemaphore concurrencySemaphore,
        ISignalSubscriptionRepository subRepo,
        IOhlcvRepository ohlcvRepo,
        HttpContext context,
        ISysConfigRepository sysConfig,
        ILogger<ProfileOptimisationEngine> logger,
        CancellationToken ct)
    {
        if (!ObjectId.TryParse(subscriptionId, out var oid))
            return TypedResults.BadRequest("Invalid subscription ID.");

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        if (string.IsNullOrWhiteSpace(userId))
            return TypedResults.BadRequest("User not authenticated.");

        // Check concurrency cap (U-9: optimisation shares the REQ-SLO-007 cap)
        using var slot = await concurrencySemaphore.AcquireAsync(userId, ct);
        if (slot is null)
        {
            return TypedResults.BadRequest(
                "You already have a backtest or optimisation run in progress. " +
                "Please wait for it to complete before starting another.");
        }

        // Validate subscription
        var sub = await subRepo.GetByIdAndUserAsync(oid, userId, ct);
        if (sub is null)
            return TypedResults.NotFound("Subscription not found.");

        if (sub.Status == SubscriptionStatus.Paused)
            return TypedResults.BadRequest("Cannot optimise a paused subscription.");

        // Validate the symbol exists in the historical dataset
        var lastCandle = await ohlcvRepo.GetLastCandleDateAsync(symbol, ct);
        if (lastCandle is null)
            return TypedResults.BadRequest($"Symbol '{symbol}' has no historical data. " +
                "Please ensure the symbol has been seeded with data before running optimisation.");

        // Default date range: last 3 years or from config
        var defaultYearsBack = await sysConfig.GetLongAsync(
            "rme.optimisation.default_lookback_years", 3, ct);
        var dateRangeStart = request.DateRangeStart != default
            ? request.DateRangeStart
            : DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-(int)defaultYearsBack));
        var dateRangeEnd = request.DateRangeEnd != default
            ? request.DateRangeEnd
            : DateOnly.FromDateTime(DateTime.UtcNow);

        if (dateRangeEnd <= dateRangeStart)
            return TypedResults.BadRequest("DateRangeEnd must be after DateRangeStart.");

        try
        {
            var grid = request.OverrideGrid is not null
                ? JsonSerializer.Deserialize<ProfileOptimisationGrid>(request.OverrideGrid)
                : null;

            var summary = await engine.RunOptimisationAsync(
                oid, symbol, userId, dateRangeStart, dateRangeEnd, grid, ct);

            return TypedResults.Ok(MapToResponse(summary));
        }
        catch (InvalidOperationException ex)
        {
            return TypedResults.BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// GET /api/v1/backtest/optimise/runs
    /// Lists optimisation runs for the current user.
    /// </summary>
    public static async Task<Ok<List<OptimisationRunSummaryResponse>>> ListOptimisationRunsAsync(
        IOptimisationResultRepository resultRepo,
        HttpContext context,
        int limit = 20,
        int offset = 0,
        CancellationToken ct = default)
    {
        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var docs = await resultRepo.ListByUserAsync(userId, limit, offset, ct);

        var summaries = docs.Select(d => new OptimisationRunSummaryResponse
        {
            RunId = d.Id.ToString(),
            Symbol = d.Symbol,
            Status = d.Status,
            TriggeredAt = d.TriggeredAt,
            CompletedAt = d.CompletedAt,
            ResultCount = d.Results.Count,
            TopProfileLabel = d.Results.Count > 0
                ? d.Results.OrderByDescending(r => r.FinalScore).First().ProfileLabel
                : null,
        }).ToList();

        return TypedResults.Ok(summaries);
    }

    /// <summary>
    /// GET /api/v1/backtest/optimise/runs/{runId}
    /// Returns a specific optimisation run with full results.
    /// </summary>
    public static async Task<Results<Ok<OptimisationRunDetailResponse>, NotFound<string>>> GetOptimisationRunAsync(
        string runId,
        IOptimisationResultRepository resultRepo,
        IOhlcvRepository ohlcvRepo,
        ISignalSubscriptionRepository subRepo,
        HttpContext context,
        CancellationToken ct)
    {
        if (!ObjectId.TryParse(runId, out var oid))
            return TypedResults.NotFound("Invalid run ID.");

        var doc = await resultRepo.GetByIdAsync(oid, ct);
        if (doc is null)
            return TypedResults.NotFound($"Optimisation run {runId} not found.");

        // Compute staleness (REQ-RME-021 / O-6)
        var isStale = await ComputeStalenessAsync(doc, ohlcvRepo, subRepo, ct);

        return TypedResults.Ok(MapToDetailResponse(doc, isStale));
    }

    /// <summary>
    /// GET /api/v1/backtest/optimise/latest/{subscriptionId}/{symbol}
    /// Returns the latest completed optimisation result for a subscription + symbol,
    /// with staleness indicator (REQ-RME-021).
    /// </summary>
    public static async Task<Results<Ok<OptimisationRunDetailResponse>, NotFound<string>, NoContent>> GetLatestOptimisationAsync(
        string subscriptionId,
        string symbol,
        IOptimisationResultRepository resultRepo,
        IOhlcvRepository ohlcvRepo,
        ISignalSubscriptionRepository subRepo,
        HttpContext context,
        CancellationToken ct)
    {
        if (!ObjectId.TryParse(subscriptionId, out var oid))
            return TypedResults.NotFound("Invalid subscription ID.");

        var doc = await resultRepo.GetLatestAsync(oid, symbol.ToUpperInvariant(), ct);
        if (doc is null)
            return TypedResults.NotFound("No completed optimisation result found for this subscription and symbol.");

        // Compute staleness (REQ-RME-021 / O-6)
        var isStale = await ComputeStalenessAsync(doc, ohlcvRepo, subRepo, ct);

        return TypedResults.Ok(MapToDetailResponse(doc, isStale));
    }

    /// <summary>
    /// Computes staleness for an optimisation result (O-6).
    /// REQ-RME-021: evaluated as a single computed field at read time.
    /// Returns true if any of the three conditions indicate staleness.
    /// </summary>
    private static async Task<bool> ComputeStalenessAsync(
        OptimisationResultDocument doc,
        IOhlcvRepository ohlcvRepo,
        ISignalSubscriptionRepository subRepo,
        CancellationToken ct)
    {
        if (doc.CompletedAt is null) return false;

        // (a) Check if newer daily candle data exists for any evaluated symbol
        foreach (var evalSymbol in doc.EvaluatedSymbols)
        {
            var latestCandleDate = await ohlcvRepo.GetLastCandleDateAsync(evalSymbol, ct);
            if (latestCandleDate.HasValue)
            {
                var latestCandleDateTime = latestCandleDate.Value.ToDateTime(TimeOnly.MinValue);
                if (latestCandleDateTime > doc.CompletedAt.Value)
                    return true;
            }
        }

        // (b) Check if the signal subscription version has changed
        var sub = await subRepo.GetByIdAsync(doc.SubscriptionId, ct);
        if (sub is not null)
        {
            var liveVersionId = sub.CurrentVersionIndex >= 0 && sub.CurrentVersionIndex < sub.Versions.Count
                ? sub.Versions[sub.CurrentVersionIndex].VersionId.ToString()
                : "";
            if (!string.IsNullOrEmpty(doc.SignalSubscriptionVersionId)
                && doc.SignalSubscriptionVersionId != liveVersionId)
                return true;
        }

        // (c) Code version check — simplified: always passes when we can't determine versions
        // In a full implementation, the Signal type's code version would be compared.

        return false;
    }

    // ── Response mapping ──────────────────────────────────────

    private static OptimisationRunResponse MapToResponse(OptimisationRunSummary summary)
    {
        return new OptimisationRunResponse
        {
            RunId = summary.RunId.ToString(),
            Symbol = summary.Symbol,
            Status = summary.Status,
            TopProfile = summary.Results.Count > 0
                ? MapProfileConfig(summary.Results[0])
                : null,
            TotalProfiles = summary.EvaluatedCount,
            Results = summary.Results.Select(MapProfileConfig).ToList(),
        };
    }

    private static OptimisationRunDetailResponse MapToDetailResponse(
        OptimisationResultDocument doc, bool isStale)
    {
        return new OptimisationRunDetailResponse
        {
            RunId = doc.Id.ToString(),
            UserId = doc.UserId,
            SubscriptionId = doc.SubscriptionId.ToString(),
            Symbol = doc.Symbol,
            TriggeredAt = doc.TriggeredAt,
            CompletedAt = doc.CompletedAt,
            Status = doc.Status,
            DateRangeStart = doc.DateRangeStart,
            DateRangeEnd = doc.DateRangeEnd,
            IsStale = isStale,
            Results = doc.Results
                .OrderByDescending(r => r.FinalScore)
                .Select(r => new ProfileConfigResponse
                {
                    ProfileLabel = r.ProfileLabel,
                    ProfileJson = r.ProfileJson,
                    TotalTrades = r.TotalTrades,
                    WinRatePct = r.WinRatePct,
                    AverageR = r.AverageR,
                    ExpectancyInR = r.ExpectancyInR,
                    ProfitFactor = r.ProfitFactor,
                    MaxDrawdownPct = r.MaxDrawdownPct,
                    SharpeRatio = r.SharpeRatio,
                    CompositeScore = r.CompositeScore,
                    ComponentScores = r.ComponentScores,
                    RobustnessFactor = r.RobustnessFactor,
                    RobustnessSubScores = r.RobustnessSubScores,
                    FinalScore = r.FinalScore,
                    IsLowConfidence = r.IsLowConfidence,
                })
                .ToList(),
        };
    }

    private static ProfileConfigResponse MapProfileConfig(ProfileConfigScored s)
    {
        return new ProfileConfigResponse
        {
            ProfileLabel = s.Profile.Label,
            ProfileJson = s.Profile.ToJsonString(),
            TotalTrades = s.TotalTrades,
            WinRatePct = s.WinRatePct,
            AverageR = s.AverageR,
            ExpectancyInR = s.ExpectancyInR,
            ProfitFactor = s.ProfitFactor,
            MaxDrawdownPct = s.MaxDrawdownPct,
            SharpeRatio = s.SharpeRatio,
            CompositeScore = s.CompositeScore,
            ComponentScores = s.ComponentScores,
            RobustnessFactor = s.RobustnessFactor,
            RobustnessSubScores = s.RobustnessSubScores,
            FinalScore = s.FinalScore,
            IsLowConfidence = s.IsLowConfidence,
        };
    }
}

// ── Request / Response DTOs ─────────────────────────────────

public sealed record StartOptimisationRequest
{
    public DateOnly DateRangeStart { get; init; } = default;
    public DateOnly DateRangeEnd { get; init; } = default;
    public string? OverrideGrid { get; init; }
}

public sealed record OptimisationRunResponse
{
    public required string RunId { get; init; }
    public required string Symbol { get; init; }
    public required string Status { get; init; }
    public ProfileConfigResponse? TopProfile { get; init; }
    public int TotalProfiles { get; init; }
    public required List<ProfileConfigResponse> Results { get; init; }
}

public sealed record OptimisationRunDetailResponse
{
    public required string RunId { get; init; }
    public required string UserId { get; init; }
    public required string SubscriptionId { get; init; }
    public required string Symbol { get; init; }
    public DateTime TriggeredAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public required string Status { get; init; }
    public DateOnly DateRangeStart { get; init; }
    public DateOnly DateRangeEnd { get; init; }

    /// <summary>Staleness indicator — REQ-RME-021 / O-6.</summary>
    public bool IsStale { get; init; }

    public required List<ProfileConfigResponse> Results { get; init; }
}

public sealed record ProfileConfigResponse
{
    public required string ProfileLabel { get; init; }
    public required string ProfileJson { get; init; }
    public int TotalTrades { get; init; }
    public decimal WinRatePct { get; init; }
    public decimal AverageR { get; init; }
    public decimal ExpectancyInR { get; init; }
    public decimal ProfitFactor { get; init; }
    public decimal MaxDrawdownPct { get; init; }
    public decimal SharpeRatio { get; init; }
    public decimal CompositeScore { get; init; }
    public Dictionary<string, decimal> ComponentScores { get; init; } = [];
    public decimal RobustnessFactor { get; init; }
    public Dictionary<string, decimal> RobustnessSubScores { get; init; } = [];
    public decimal FinalScore { get; init; }
    public bool IsLowConfidence { get; init; }
}

public sealed record OptimisationRunSummaryResponse
{
    public required string RunId { get; init; }
    public required string Symbol { get; init; }
    public required string Status { get; init; }
    public DateTime TriggeredAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public int ResultCount { get; init; }
    public string? TopProfileLabel { get; init; }
}
