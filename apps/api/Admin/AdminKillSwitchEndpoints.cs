using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.SysConfig;
using SignalStack.Api.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin endpoints for the global RME kill switch (REQ-ADMIN-007).
/// All mutations require step-up re-authentication (REQ-SEC-011).
/// </summary>
public static class AdminKillSwitchEndpoints
{
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapAdminKillSwitchEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/kill-switch
        // Returns the current global kill switch status. (REQ-ADMIN-007)
        // Reads from sys_config risk.kill_switch.active. No step-up required for reads.
        admin.MapGet("/kill-switch", async (
            IMongoDatabase database) =>
        {
            var sysConfig = database.GetCollection<BsonDocument>("sys_config");

            var doc = await sysConfig
                .Find(Builders<BsonDocument>.Filter.Eq("key", "risk.kill_switch.active"))
                .FirstOrDefaultAsync();

            var isActive = doc is not null
                && doc.Contains("value")
                && doc["value"] is BsonBoolean b
                && b.Value;

            return Results.Ok(new
            {
                kill_switch_active = isActive,
                key = "risk.kill_switch.active",
            });
        }).RequireAuthorization();

        // POST /api/v1/admin/kill-switch/activate
        // Activates the global RME kill switch (step-up gated).
        // When active, all non-terminal positions transition to Suspended
        // with reason kill_switch_activated (enforced by RME channel consumer).
        // REQ-ADMIN-007: requires step-up.
        admin.MapPost("/kill-switch/activate", async (
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            ISysConfigRepository configRepo) =>
        {
            return await ToggleKillSwitchAsync(
                context, sessionRepo, auditRepo, userRepo, configRepo,
                activate: true);
        }).RequireAuthorization();

        // POST /api/v1/admin/kill-switch/deactivate
        // Deactivates the global RME kill switch (step-up gated).
        // Positions are NOT auto-released — a fresh EODSR run is required
        // before any advisory resumes (REQ-ADMIN-007).
        admin.MapPost("/kill-switch/deactivate", async (
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            ISysConfigRepository configRepo) =>
        {
            return await ToggleKillSwitchAsync(
                context, sessionRepo, auditRepo, userRepo, configRepo,
                activate: false);
        }).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ToggleKillSwitchAsync(
        HttpContext context,
        ISessionRepository sessionRepo,
        IAuditEventRepository auditRepo,
        IUserRepository userRepo,
        ISysConfigRepository configRepo,
        bool activate)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

        // Verify admin role.
        var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
        if (user is null || user.Role != UserRole.Admin)
            return Results.Forbid();

        // REQ-SEC-011: validate step-up re-authentication within 5 minutes.
        var jti = context.User.FindFirst("jti")?.Value ?? "";
        var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
        var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
            && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
        if (!stepUpValid)
        {
            return Results.Json(
                new { error = "step_up_required" },
                statusCode: StatusCodes.Status401Unauthorized);
        }

        var now = DateTime.UtcNow;
        var action = activate ? "activate" : "deactivate";
        var actionType = activate
            ? "kill_switch_activated"
            : "kill_switch_deactivated";

        // Write audit event (REQ-SEC-011 / REQ-CONFIG-005a pattern).
        await auditRepo.RecordStepUpGatedAsync(
            userId,
            actionType,
            now,
            session!.StepUpEventId!.Value,
            details: new Dictionary<string, object?>
            {
                ["action"] = action,
                ["kill_switch_key"] = "risk.kill_switch.active",
            },
            cancellationToken: context.RequestAborted);

        // Update sys_config via repository (REQ-CONFIG-005).
        await configRepo.UpdateAsync(
            "risk.kill_switch.active",
            BsonBoolean.Create(activate),
            userId,
            context.RequestAborted);

        return Results.Ok(new
        {
            kill_switch_active = activate,
            message = activate
                ? "Global RME kill switch activated. All positions will be suspended on next RME cycle."
                : "Global RME kill switch deactivated. Positions remain suspended until a fresh EODSR run."
        });
    }
}
