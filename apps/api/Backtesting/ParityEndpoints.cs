using Microsoft.AspNetCore.Http.HttpResults;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Notifications;
using SignalStack.Signals.Backtesting;
using SignalStack.Storage.SysConfig;

namespace SignalStack.Api.Backtesting;

/// <summary>
/// API endpoints for the BTE-vs-RME parity check.
/// REQ-NFR-017: nightly parity check with admin alert on divergence.
/// </summary>
public static class ParityEndpoints
{
    public static RouteGroupBuilder MapParityEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/parity", RunParityCheckAsync);
        return group;
    }

    /// <summary>
    /// POST /api/v1/backtest/parity
    ///
    /// Triggers a BTE-vs-RME parity check against the built-in synthetic fixture.
    /// On divergence, an admin notification + audit event is recorded.
    /// Returns the parity result with per-scenario divergence details.
    ///
    /// Requires admin role. Respects <c>operations.bte_rme_parity.enabled</c> toggle.
    /// </summary>
    public static async Task<Results<Ok<BteRmeParityResult>, BadRequest<string>>> RunParityCheckAsync(
        BteRmeParityService parityService,
        HttpContext context,
        CancellationToken ct)
    {
        // Require admin role
        if (!context.User.IsInRole("admin"))
        {
            return TypedResults.BadRequest("Only admins can trigger the parity check.");
        }

        try
        {
            var result = await parityService.RunParityCheckAsync(ct);
            return TypedResults.Ok(result);
        }
        catch (Exception ex)
        {
            return TypedResults.BadRequest($"Parity check failed: {ex.Message}");
        }
    }
}
