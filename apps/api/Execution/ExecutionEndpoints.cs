using System.Security.Claims;
using SignalStack.Api.Execution;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Execution assistance API endpoints (Phase 7).
/// Wires pre-flight checks and order context data for the Phase 1 modal.
/// P7-T3: Pre-flight check endpoint.
/// P7-T4: Order context endpoint.
/// </summary>
public static class ExecutionEndpoints
{
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var execution = app.MapGroup("/api/v1/execution").RequireAuthorization();

        // ── GET /api/v1/execution/pre-flight?symbol={symbol} ──────────────
        // Returns a structured set of checks the user must pass before the
        // FYERS execution widget is presented. P7-T3.
        execution.MapGet("/pre-flight", async (
            HttpContext context,
            PreFlightCheckService preFlightService,
            string? symbol) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var result = await preFlightService.CheckAsync(
                userId, symbol, context.RequestAborted);

            return Results.Ok(new
            {
                can_execute = result.CanExecute,
                checks = result.Checks.Select(c => new
                {
                    check = c.Check,
                    passed = c.Passed,
                    severity = c.Severity,
                    message = c.Message,
                }),
                blocking_count = result.Checks.Count(c => !c.Passed && c.Severity == "blocking"),
                warning_count = result.Checks.Count(c => !c.Passed && c.Severity == "warning"),
            });
        });

        // ── GET /api/v1/execution/order-context?symbol={symbol}&action={action} ─
        // Returns RME sizing, portfolio heat, stop levels, and market state
        // for the Phase 1 platform modal. P7-T4 / REQ-ORDER-007/008.
        execution.MapGet("/order-context", async (
            HttpContext context,
            OrderContextService orderContextService,
            string symbol,
            string action) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new { error = "symbol query parameter is required." });

            var validActions = new[] { "entry", "add", "reduce", "exit" };
            if (!validActions.Contains(action.ToLowerInvariant()))
                return Results.BadRequest(new { error = $"action must be one of: {string.Join(", ", validActions)}" });

            var result = await orderContextService.GetOrderContextAsync(
                userId, symbol, action, context.RequestAborted);

            return Results.Ok(new
            {
                symbol = result.Symbol,
                action_type = result.ActionType,
                recommended_quantity = result.RecommendedQuantity,
                current_price = result.CurrentPrice,
                stop_level = result.StopLevel,
                trailing_stop_level = result.TrailingStopLevel,
                trailing_stop_active = result.TrailingStopActive,
                portfolio_heat_before_pct = result.PortfolioHeatBeforePct,
                portfolio_heat_after_pct = result.PortfolioHeatAfterPct,
                max_heat_pct = result.MaxHeatPct,
                account_equity = result.AccountEquity,
                drawdown_pct = result.DrawdownPct,
                entry_blocked = result.EntryBlocked,
                entry_blocked_reason = result.EntryBlockedReason,
                is_market_halted = result.IsMarketHalted,
                in_market_hours = result.InMarketHours,
                market_hours_message = result.MarketHoursMessage,
                circuit_limit_direction = result.CircuitLimitDirection,
                circuit_limit_active = result.CircuitLimitActive,
                circuit_limit_stale = result.CircuitLimitStale,
                has_position = result.HasPosition,
                position_state = result.PositionState,
            });
        });

        return app;
    }
}
