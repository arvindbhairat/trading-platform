using System.Security.Claims;
using SignalStack.Api.Execution;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Execution assistance API endpoints (Phase 7).
/// Wires pre-flight checks and, in later tasks, order intent and signed-payload flows.
/// P7-T3: Pre-flight check endpoint.
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

        return app;
    }
}
