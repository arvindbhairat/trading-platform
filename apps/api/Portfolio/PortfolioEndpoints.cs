using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Portfolio;
using SignalStack.Storage.Portfolio;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Portfolio API endpoints for holdings, PnL, and portfolio summary.
///
/// REQ-DASH-002: portfolio summary (total invested, market value, return).
/// REQ-DASH-010: positions summary with per-position data.
/// REQ-DASH-013: data freshness reflected in price source timestamps.
/// </summary>
public static class PortfolioEndpoints
{
    public static IEndpointRouteBuilder MapPortfolioEndpoints(this IEndpointRouteBuilder app)
    {
        var portfolio = app.MapGroup("/api/v1/portfolio").RequireAuthorization();

        // ── GET /api/v1/portfolio/holdings ─────────────────────────────────
        // Returns all open holdings with current PnL data.
        // REQ-DASH-010: symbol, quantity, avg buy, current price, market value, PnL.
        // REQ-PLC-010(c): corporate_action_warning_active flag per holding.
        portfolio.MapGet("/holdings", async (
            HttpContext context,
            HoldingsService holdingsService,
            IMongoDatabase database) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var holdings = await holdingsService.ComputeHoldingsAsync(
                userId, context.RequestAborted);

            // Build set of symbols with open CA suspension incidents.
            var caSymbols = await GetCaSuspensionSymbolsAsync(
                database, userId, context.RequestAborted);

            var result = holdings.Select(h => new HoldingResponse(
                Symbol: h.Symbol,
                Quantity: h.Quantity,
                AverageBuyPrice: h.AverageBuyPrice,
                TotalInvested: h.TotalInvested,
                CurrentPrice: h.CurrentPrice,
                CurrentMarketValue: h.CurrentMarketValue,
                UnrealizedPnl: h.UnrealizedPnl,
                UnrealizedPnlPercent: h.UnrealizedPnlPercent,
                HoldingPeriodDays: h.HoldingPeriodDays,
                CorporateActionWarningActive: caSymbols.Contains(h.Symbol)
            )).ToList();

            return Results.Ok(new HoldingsListResponse(Holdings: result));
        });

        // ── GET /api/v1/portfolio/holdings/{symbol} ───────────────────────
        // Returns holding detail for a single symbol.
        // REQ-PLC-010(c): corporate_action_warning_active flag.
        portfolio.MapGet("/holdings/{symbol}", async (
            string symbol,
            HttpContext context,
            HoldingsService holdingsService,
            IMongoDatabase database) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var holding = await holdingsService.ComputeHoldingAsync(
                userId, symbol, context.RequestAborted);

            if (holding is null)
                return Results.NotFound(new { error = $"No open holding found for symbol '{symbol}'." });

            var caSymbols = await GetCaSuspensionSymbolsAsync(
                database, userId, context.RequestAborted);

            return Results.Ok(new HoldingDetailResponse(
                Symbol: holding.Symbol,
                Quantity: holding.Quantity,
                AverageBuyPrice: holding.AverageBuyPrice,
                TotalInvested: holding.TotalInvested,
                CurrentPrice: holding.CurrentPrice,
                CurrentMarketValue: holding.CurrentMarketValue,
                UnrealizedPnl: holding.UnrealizedPnl,
                UnrealizedPnlPercent: holding.UnrealizedPnlPercent,
                HoldingPeriodDays: holding.HoldingPeriodDays,
                CorporateActionWarningActive: caSymbols.Contains(holding.Symbol),
                Lots: holding.Lots.Select(l => new LotResponse(
                    TradeId: l.TradeId,
                    RemainingQuantity: l.RemainingQuantity,
                    BuyPrice: l.BuyPrice,
                    BuyDate: l.BuyDate
                ))
            ));
        });

        // ── GET /api/v1/portfolio/impact ──────────────────────────────────
        // Returns pre-trade portfolio impact (heat, sector exposure, entry status).
        // P6-T22 / portfolio-risk-guidelines § Required Auditability.
        portfolio.MapGet("/impact", async (
            HttpContext context,
            PortfolioImpactService impactService) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var impact = await impactService.ComputeImpactAsync(
                userId, context.RequestAborted);

            return Results.Ok(new PortfolioImpactResponse(
                PortfolioHeatPct: impact.PortfolioHeatPct,
                MaxHeatPct: impact.MaxHeatPct,
                TotalOpenRisk: impact.TotalOpenRisk,
                AccountEquity: impact.AccountEquity,
                OpenPositionCount: impact.OpenPositionCount,
                SuspendedPositionCount: impact.SuspendedPositionCount,
                EntryBlocked: impact.EntryBlocked,
                EntryBlockedReason: impact.EntryBlockedReason,
                SectorExposures: impact.SectorExposures?.Select(s => new SectorExposureResponse(
                    Sector: s.Sector,
                    ExposurePct: s.ExposurePct,
                    OpenRisk: s.OpenRisk
                ))
            ));
        });

        // ── GET /api/v1/portfolio/summary ─────────────────────────────────
        // Returns portfolio-level KPIs.
        // REQ-DASH-002: total invested, market value, return.
        portfolio.MapGet("/summary", async (
            HttpContext context,
            HoldingsService holdingsService) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var summary = await holdingsService.ComputeSummaryAsync(
                userId, context.RequestAborted);

            return Results.Ok(new PortfolioSummaryResponse(
                TotalInvested: summary.TotalInvested,
                TotalMarketValue: summary.TotalMarketValue,
                TotalUnrealizedPnl: summary.TotalUnrealizedPnl,
                TotalUnrealizedPnlPercent: summary.TotalUnrealizedPnlPercent,
                PositionCount: summary.PositionCount
            ));
        });

        return app;
    }

    // ── Response DTOs ────────────────────────────────────────────────────────

    public sealed record HoldingResponse(
        string Symbol,
        int Quantity,
        decimal AverageBuyPrice,
        decimal TotalInvested,
        decimal? CurrentPrice,
        decimal? CurrentMarketValue,
        decimal? UnrealizedPnl,
        decimal? UnrealizedPnlPercent,
        int HoldingPeriodDays,
        bool CorporateActionWarningActive
    );

    public sealed record HoldingsListResponse(IEnumerable<HoldingResponse> Holdings);

    public sealed record LotResponse(
        string TradeId,
        int RemainingQuantity,
        decimal BuyPrice,
        DateTime BuyDate
    );

    public sealed record HoldingDetailResponse(
        string Symbol,
        int Quantity,
        decimal AverageBuyPrice,
        decimal TotalInvested,
        decimal? CurrentPrice,
        decimal? CurrentMarketValue,
        decimal? UnrealizedPnl,
        decimal? UnrealizedPnlPercent,
        int HoldingPeriodDays,
        bool CorporateActionWarningActive,
        IEnumerable<LotResponse> Lots
    );

    public sealed record PortfolioImpactResponse(
        decimal PortfolioHeatPct,
        decimal MaxHeatPct,
        decimal TotalOpenRisk,
        decimal AccountEquity,
        int OpenPositionCount,
        int SuspendedPositionCount,
        bool EntryBlocked,
        string? EntryBlockedReason,
        IEnumerable<SectorExposureResponse>? SectorExposures
    );

    public sealed record SectorExposureResponse(
        string Sector,
        decimal ExposurePct,
        decimal OpenRisk
    );

    public sealed record PortfolioSummaryResponse(
        decimal TotalInvested,
        decimal? TotalMarketValue,
        decimal? TotalUnrealizedPnl,
        decimal? TotalUnrealizedPnlPercent,
        int PositionCount
    );

    /// <summary>
    /// Returns the set of symbols that have open corporate_action_suspension
    /// incidents for the given user (REQ-PLC-010(c)).
    /// </summary>
    private static async Task<HashSet<string>> GetCaSuspensionSymbolsAsync(
        IMongoDatabase database, string userId, CancellationToken ct)
    {
        // Get this user's positions to build a position_id → symbol map.
        var positions = database.GetCollection<BsonDocument>("positions");
        var userPositionFilter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
        var userPositions = await positions.Find(userPositionFilter)
            .Project(Builders<BsonDocument>.Projection
                .Include("_id")
                .Include("symbol"))
            .ToListAsync(ct);

        if (userPositions.Count == 0)
            return [];

        var posIdToSymbol = new Dictionary<string, string>();
        foreach (var pos in userPositions)
        {
            var posId = pos["_id"].AsGuid.ToString();
            var symbol = pos.GetValue("symbol", "").AsString;
            if (!string.IsNullOrEmpty(symbol))
                posIdToSymbol[posId] = symbol;
        }

        if (posIdToSymbol.Count == 0)
            return [];

        // Find open CA suspension incidents for this user's positions.
        var incidents = database.GetCollection<BsonDocument>("rme_incidents");
        var incidentFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.In("position_id", posIdToSymbol.Keys),
            Builders<BsonDocument>.Filter.Eq("incident_type", "corporate_action_suspension"),
            Builders<BsonDocument>.Filter.Eq("status", "open"));
        var caIncidents = await incidents.Find(incidentFilter).ToListAsync(ct);

        return caIncidents
            .Select(i => i.GetValue("position_id", BsonNull.Value)?.AsString)
            .Where(id => id is not null && posIdToSymbol.ContainsKey(id))
            .Select(id => posIdToSymbol[id!])
            .ToHashSet();
    }
}
