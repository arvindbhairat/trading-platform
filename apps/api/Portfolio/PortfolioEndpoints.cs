using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Portfolio;

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

            var result = holdings.Select(h => new
            {
                symbol = h.Symbol,
                quantity = h.Quantity,
                average_buy_price = h.AverageBuyPrice,
                total_invested = h.TotalInvested,
                current_price = h.CurrentPrice,
                current_market_value = h.CurrentMarketValue,
                unrealized_pnl = h.UnrealizedPnl,
                unrealized_pnl_percent = h.UnrealizedPnlPercent,
                holding_period_days = h.HoldingPeriodDays,
                corporate_action_warning_active = caSymbols.Contains(h.Symbol),
            }).ToList();

            return Results.Ok(new { holdings = result });
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

            return Results.Ok(new
            {
                symbol = holding.Symbol,
                quantity = holding.Quantity,
                average_buy_price = holding.AverageBuyPrice,
                total_invested = holding.TotalInvested,
                current_price = holding.CurrentPrice,
                current_market_value = holding.CurrentMarketValue,
                unrealized_pnl = holding.UnrealizedPnl,
                unrealized_pnl_percent = holding.UnrealizedPnlPercent,
                holding_period_days = holding.HoldingPeriodDays,
                corporate_action_warning_active = caSymbols.Contains(holding.Symbol),
                lots = holding.Lots.Select(l => new
                {
                    trade_id = l.TradeId,
                    remaining_quantity = l.RemainingQuantity,
                    buy_price = l.BuyPrice,
                    buy_date = l.BuyDate,
                }),
            });
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

            return Results.Ok(new
            {
                portfolio_heat_pct = impact.PortfolioHeatPct,
                max_heat_pct = impact.MaxHeatPct,
                total_open_risk = impact.TotalOpenRisk,
                account_equity = impact.AccountEquity,
                open_position_count = impact.OpenPositionCount,
                suspended_position_count = impact.SuspendedPositionCount,
                entry_blocked = impact.EntryBlocked,
                entry_blocked_reason = impact.EntryBlockedReason,
                sector_exposures = impact.SectorExposures?.Select(s => new
                {
                    sector = s.Sector,
                    exposure_pct = s.ExposurePct,
                    open_risk = s.OpenRisk,
                }),
            });
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

            return Results.Ok(new
            {
                total_invested = summary.TotalInvested,
                total_market_value = summary.TotalMarketValue,
                total_unrealized_pnl = summary.TotalUnrealizedPnl,
                total_unrealized_pnl_percent = summary.TotalUnrealizedPnlPercent,
                position_count = summary.PositionCount,
            });
        });

        return app;
    }

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
