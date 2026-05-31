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

        // ── GET /api/v1/rme/advisory?symbol={symbol} ────────────────────
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
                return Results.BadRequest(new ErrorResponse("symbol query parameter is required."));

            var advisory = await advisoryService.ComputeAdvisoryAsync(
                userId, symbol, context.RequestAborted);

            return Results.Ok(advisory);
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

            return Results.Ok(health);
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

            return Results.Ok(status);
        });

        return app;
    }
}

internal sealed record ErrorResponse(string Error);
