using System.Security.Claims;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin "View as user" impersonation endpoints.
/// REQ-ADMIN-015: step-up gated, read-only enforcement, full audit trail, idle timeout.
/// REQ-PRIVACY-012: Privacy Policy must disclose that admin may access read-only view
/// of any user's account for operational support and that each access is recorded.
/// </summary>
public static class ImpersonationEndpoints
{
    // Matches the 5-minute window in AuthEndpoints (REQ-SEC-011).
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapImpersonationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // POST /api/v1/admin/impersonation/start
        // REQ-ADMIN-015: begins impersonating the target user.
        // Step-up gated (REQ-SEC-011). Records audit event. Idle timeout starts now.
        admin.MapPost("/impersonation/start", async (
            HttpContext context,
            IUserRepository userRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo) =>
        {
            var adminUserId = GetUserId(context);
            var adminUser = await userRepo.FindByUserIdAsync(adminUserId, context.RequestAborted);
            if (adminUser is null || adminUser.Role != UserRole.Admin)
                return Results.Forbid();

            // REQ-SEC-011: validate step-up within 5 minutes.
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var adminSession = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = adminSession?.StepUpAuthenticatedAt.HasValue == true
                && adminSession.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
            {
                return Results.Json(
                    new { error = "step_up_required" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var body = await context.Request.ReadFromJsonAsync<StartImpersonationRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null || string.IsNullOrWhiteSpace(body.TargetUserId))
            {
                return Results.BadRequest(new { error = "target_user_id_required" });
            }

            // Validate target user exists.
            var targetUser = await userRepo.FindByUserIdAsync(
                body.TargetUserId, context.RequestAborted);
            if (targetUser is null)
            {
                return Results.NotFound(new { error = "target_user_not_found" });
            }

            // Prevent self-impersonation.
            if (body.TargetUserId == adminUserId)
            {
                return Results.BadRequest(new { error = "cannot_impersonate_self" });
            }

            // REQ-PRIVACY-012 / REQ-PRIVACY-004: record that impersonation was performed
            // with the target user's ID for audit trail purposes.
            var now = DateTime.UtcNow;
            var stepUpEventId = adminSession!.StepUpEventId;

            var auditId = await auditRepo.RecordStepUpGatedAsync(
                adminUserId,
                "impersonation_started",
                now,
                stepUpEventId ?? ObjectId.Empty,
                details: new Dictionary<string, object?>
                {
                    ["target_user_id"] = body.TargetUserId,
                    ["target_email"] = targetUser.Email,
                    ["impersonation_idle_timeout_minutes"] = 15,
                },
                cancellationToken: context.RequestAborted);

            // Store impersonation state on the admin's session.
            await sessionRepo.StartImpersonationAsync(
                jti, body.TargetUserId, now, context.RequestAborted);

            return Results.Ok(new
            {
                started = true,
                target_user_id = body.TargetUserId,
                target_display_name = targetUser.DisplayName,
                started_at = now.ToString("o"),
                idle_timeout_minutes = 15,
            });
        }).RequireAuthorization();

        // POST /api/v1/admin/impersonation/stop
        // REQ-ADMIN-015: stops the active impersonation session.
        admin.MapPost("/impersonation/stop", async (
            HttpContext context,
            IUserRepository userRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo) =>
        {
            var adminUserId = GetUserId(context);
            var adminUser = await userRepo.FindByUserIdAsync(adminUserId, context.RequestAborted);
            if (adminUser is null || adminUser.Role != UserRole.Admin)
                return Results.Forbid();

            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var adminSession = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            if (adminSession is null)
                return Results.Unauthorized();

            if (string.IsNullOrEmpty(adminSession.ImpersonatingUserId))
            {
                return Results.BadRequest(new { error = "not_impersonating" });
            }

            var now = DateTime.UtcNow;
            var duration = adminSession.ImpersonationStartedAt.HasValue
                ? (now - adminSession.ImpersonationStartedAt.Value).TotalSeconds
                : 0;

            // Record audit event before clearing (REQ-CONFIG-005a pattern).
            await auditRepo.RecordAsync(
                adminUserId,
                "impersonation_stopped",
                now,
                details: new Dictionary<string, object?>
                {
                    ["target_user_id"] = adminSession.ImpersonatingUserId,
                    ["duration_seconds"] = (int)duration,
                },
                cancellationToken: context.RequestAborted);

            await sessionRepo.StopImpersonationAsync(jti, context.RequestAborted);

            return Results.Ok(new { stopped = true });
        }).RequireAuthorization();

        // GET /api/v1/admin/impersonation/status
        // REQ-ADMIN-015: returns current impersonation state for the admin session,
        // including idle-timeout information.
        admin.MapGet("/impersonation/status", async (
            HttpContext context,
            IUserRepository userRepo,
            ISessionRepository sessionRepo) =>
        {
            var adminUserId = GetUserId(context);
            var adminUser = await userRepo.FindByUserIdAsync(adminUserId, context.RequestAborted);
            if (adminUser is null || adminUser.Role != UserRole.Admin)
                return Results.Forbid();

            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var adminSession = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            if (adminSession is null)
                return Results.Unauthorized();

            if (string.IsNullOrEmpty(adminSession.ImpersonatingUserId))
            {
                return Results.Ok(new
                {
                    active = false,
                });
            }

            // Check idle timeout (default 15 min).
            const int idleTimeoutMinutes = 15;
            var idleExpired = adminSession.ImpersonationLastActivityAt.HasValue
                && DateTime.UtcNow - adminSession.ImpersonationLastActivityAt.Value
                   > TimeSpan.FromMinutes(idleTimeoutMinutes);

            // Resolve target user display name.
            var targetUser = await userRepo.FindByUserIdAsync(
                adminSession.ImpersonatingUserId, context.RequestAborted);

            return Results.Ok(new
            {
                active = !idleExpired,
                idle_expired = idleExpired,
                target_user_id = adminSession.ImpersonatingUserId,
                target_display_name = targetUser?.DisplayName,
                started_at = adminSession.ImpersonationStartedAt?.ToString("o"),
                last_activity_at = adminSession.ImpersonationLastActivityAt?.ToString("o"),
                idle_timeout_minutes = idleTimeoutMinutes,
            });
        }).RequireAuthorization();

        return app;
    }

    private static string GetUserId(HttpContext context)
    {
        return context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";
    }
}

internal sealed record StartImpersonationRequest(string TargetUserId);
