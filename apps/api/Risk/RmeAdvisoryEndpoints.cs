using System.Security.Claims;
using SignalStack.Api.Risk;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// RME advisory API endpoints for the chart page advisory panel.
/// P6-T27 / REQ-CHART-* (advisory subset), RME-L1.
/// </summary>
public static class RmeAdvisoryEndpoints
{
    public static IEndpointRouteBuilder MapRmeAdvisoryEndpoints(this IEndpointRouteBuilder app)
    {
        var rme = app.MapGroup("/api/v1/rme").RequireAuthorization();

        // ── GET /api/v1/rme/advisory?symbol=NSE:SBIN-EQ ──────────────────
        // Returns RME advisory data for the selected symbol.
        // P6-T27 / REQ-CHART-003, REQ-CHART-004.
        rme.MapGet("/advisory", async (
            HttpContext context,
            RmeAdvisoryService advisoryService,
            string? symbol) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { error = "symbol query parameter is required." });

            var advisory = await advisoryService.ComputeAdvisoryAsync(
                userId, symbol, context.RequestAborted);

            return Results.Ok(new
            {
                position_exists = advisory.PositionExists,
                state = advisory.State,
                entry_price = advisory.EntryPrice,
                average_entry_price = advisory.AverageEntryPrice,
                quantity = advisory.Quantity,
                stop_loss = advisory.StopLoss,
                active_stop = advisory.ActiveStop,
                trailing_stop = advisory.TrailingStop,
                trailing_stop_active = advisory.TrailingStopActive,
                current_r_multiple = advisory.CurrentRMultiple,
                unrealized_pnl = advisory.UnrealizedPnl,
                risk_amount = advisory.RiskAmount,
                add_advisory_active = advisory.AddAdvisoryActive,
                reduce_advisory_active = advisory.ReduceAdvisoryActive,
                exit_advisory_active = advisory.ExitAdvisoryActive,
                add_level = advisory.AddLevel,
                reduce_level = advisory.ReduceLevel,
                time_stop_date = advisory.TimeStopDate,
                open_position_count = advisory.OpenPositionCount,
                subscription_name = advisory.SubscriptionName,
            });
        });

        // ── GET /api/v1/rme/health ────────────────────────────────────────
        // Returns portfolio health data for the persistent health strip.
        // P6-T27: equity, heat, drawdown, position count, overall status.
        rme.MapGet("/health", async (
            HttpContext context,
            PortfolioHealthService healthService) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var health = await healthService.ComputeHealthAsync(
                userId, context.RequestAborted);

            return Results.Ok(new
            {
                account_equity = health.AccountEquity,
                portfolio_heat_pct = health.PortfolioHeatPct,
                max_heat_pct = health.MaxHeatPct,
                drawdown_pct = health.DrawdownPct,
                high_water_mark = health.HighWaterMark,
                open_position_count = health.OpenPositionCount,
                entry_blocked = health.EntryBlocked,
                entry_blocked_reason = health.EntryBlockedReason,
                drawdown_advisory = health.DrawdownAdvisory,
                health_status = health.HealthStatus,
                data_freshness_timestamp = health.DataFreshnessTimestamp,
                drawdown_mode_level = health.DrawdownModeLevel,
            });
        });

        // ── GET /api/v1/rme/channel-status ───────────────────────────────
        // Returns channel registry capacity and usage.
        // RME-L1: idle-channel cap warning at 80% of max.
        rme.MapGet("/channel-status", async (
            HttpContext context,
            RmeChannelStatusService channelStatusService) =>
        {
            var status = await channelStatusService.GetChannelStatusAsync(
                context.RequestAborted);

            return Results.Ok(new
            {
                active_position_count = status.ActivePositionCount,
                max_active_positions = status.MaxActivePositions,
                usage_pct = status.UsagePct,
                warning_active = status.WarningActive,
                warning_message = status.WarningMessage,
            });
        });

        return app;
    }
}
