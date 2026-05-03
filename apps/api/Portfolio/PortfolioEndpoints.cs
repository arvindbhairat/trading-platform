using System.Security.Claims;
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
        portfolio.MapGet("/holdings", async (
            HttpContext context,
            HoldingsService holdingsService) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var holdings = await holdingsService.ComputeHoldingsAsync(
                userId, context.RequestAborted);

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
            }).ToList();

            return Results.Ok(new { holdings = result });
        });

        // ── GET /api/v1/portfolio/holdings/{symbol} ───────────────────────
        // Returns holding detail for a single symbol.
        portfolio.MapGet("/holdings/{symbol}", async (
            string symbol,
            HttpContext context,
            HoldingsService holdingsService) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var holding = await holdingsService.ComputeHoldingAsync(
                userId, symbol, context.RequestAborted);

            if (holding is null)
                return Results.NotFound(new { error = $"No open holding found for symbol '{symbol}'." });

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
                lots = holding.Lots.Select(l => new
                {
                    trade_id = l.TradeId,
                    remaining_quantity = l.RemainingQuantity,
                    buy_price = l.BuyPrice,
                    buy_date = l.BuyDate,
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
}
