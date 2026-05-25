using System.Security.Claims;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Storage.SysConfig;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin REST endpoints for operating phase transition, gating, and SEBI
/// classification opinion management. P8-T3 — REQ-LEGAL-001/005/005a.
/// </summary>
public static class AdminPhaseEndpoints
{
    // REQ-SEC-011: these require step-up re-authentication.
    private static readonly HashSet<string> SensitiveCategories = ["legal", "operations"];

    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapAdminPhaseEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/phase/gates?target=B|C
        // Returns all gating conditions for the target phase with their status.
        // REQ-LEGAL-005: admin visibility into gate readiness.
        admin.MapGet("/phase/gates", async (
            HttpContext context,
            PhaseGateService gateSvc,
            IUserRepository userRepo,
            string? target) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            if (target is not ("B" or "C"))
                return Results.BadRequest(new { error = "target must be 'B' or 'C'" });

            var gates = await gateSvc.GetGatesAsync(target, context.RequestAborted);
            return Results.Ok(new { target_phase = target, gates });
        }).RequireAuthorization();

        // POST /api/v1/admin/phase/transition
        // Attempts a phase transition after validating all gating conditions.
        // REQ-LEGAL-001: recorded in audit_events with outgoing/incoming phase,
        // timestamp, actor, and justification.
        // REQ-SEC-011: step-up required (operations category).
        admin.MapPost("/phase/transition", async (
            HttpContext context,
            PhaseGateService gateSvc,
            ISysConfigRepository configRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo) =>
        {
            var userId = GetUserId(context);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var body = await context.Request.ReadFromJsonAsync<PhaseTransitionRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new { error = "invalid_body" });

            if (string.IsNullOrWhiteSpace(body.TargetPhase) ||
                body.TargetPhase is not ("B" or "C"))
                return Results.BadRequest(new { error = "target_phase must be 'B' or 'C'" });

            if (string.IsNullOrWhiteSpace(body.Justification))
                return Results.BadRequest(new { error = "justification_required" });

            // REQ-SEC-011: step-up required for operations category.
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new { error = "step_up_required", category = "operations" },
                    statusCode: StatusCodes.Status401Unauthorized);

            var currentPhase = await configRepo.GetCurrentPhaseAsync(context.RequestAborted);

            // Validate the transition gates.
            var result = await gateSvc.ValidateTransitionAsync(
                currentPhase, body.TargetPhase, context.RequestAborted);

            if (!result.Allowed)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "phase_transition_blocked",
                    message = result.Message,
                    failed_gates = result.FailedGates.Select(g => new
                    {
                        config_key = g.ConfigKey,
                        label = g.Label,
                        passed = g.Passed
                    })
                });
            }

            // Record audit event BEFORE mutation (REQ-CONFIG-005a pattern).
            await auditRepo.RecordStepUpGatedAsync(
                userId,
                "phase_transition",
                DateTime.UtcNow,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["from_phase"] = currentPhase,
                    ["to_phase"] = body.TargetPhase,
                    ["justification"] = body.Justification
                },
                cancellationToken: context.RequestAborted);

            // Execute the transition.
            await configRepo.UpdateAsync(
                "operations.phase.current", body.TargetPhase, userId, context.RequestAborted);

            return Results.Ok(new
            {
                success = true,
                from_phase = currentPhase,
                to_phase = body.TargetPhase
            });
        }).RequireAuthorization();

        // POST /api/v1/admin/phase/sebi-opinion
        // Records receipt of the SEBI classification opinion (REQ-LEGAL-005a).
        // Requires step-up (legal category).
        admin.MapPost("/phase/sebi-opinion", async (
            HttpContext context,
            PhaseGateService gateSvc,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo) =>
        {
            var userId = GetUserId(context);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var body = await context.Request.ReadFromJsonAsync<SebiOpinionRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new { error = "invalid_body" });

            if (string.IsNullOrWhiteSpace(body.DocumentRef))
                return Results.BadRequest(new { error = "document_ref_required" });
            if (string.IsNullOrWhiteSpace(body.LawyerName))
                return Results.BadRequest(new { error = "lawyer_name_required" });
            if (string.IsNullOrWhiteSpace(body.LawyerSebiReg))
                return Results.BadRequest(new { error = "lawyer_sebi_reg_required" });

            // REQ-SEC-011: step-up required (legal category).
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new { error = "step_up_required", category = "legal" },
                    statusCode: StatusCodes.Status401Unauthorized);

            var receivedDate = body.ReceivedDate ?? DateOnly.FromDateTime(DateTime.UtcNow);

            await gateSvc.RecordSebiOpinionAsync(
                userId, body.DocumentRef, body.LawyerName,
                body.LawyerSebiReg, receivedDate, context.RequestAborted);

            return Results.Ok(new
            {
                success = true,
                document_ref = body.DocumentRef,
                lawyer_name = body.LawyerName,
                received_date = receivedDate.ToString("yyyy-MM-dd")
            });
        }).RequireAuthorization();

        // PATCH /api/v1/admin/phase/gates/{configKey}
        // Toggle an individual gate condition (set passed = true/false).
        // REQ-LEGAL-005: admin marks gates as satisfied.
        admin.MapPatch("/phase/gates/{configKey}", async (
            string configKey,
            HttpContext context,
            PhaseGateService gateSvc,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo) =>
        {
            var userId = GetUserId(context);
            if (string.IsNullOrEmpty(userId))
                return Results.Unauthorized();

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var body = await context.Request.ReadFromJsonAsync<GateUpdateRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new { error = "invalid_body" });

            // REQ-SEC-011: step-up required (legal category).
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new { error = "step_up_required", category = "legal" },
                    statusCode: StatusCodes.Status401Unauthorized);

            await gateSvc.SetGateAsync(
                userId, configKey, body.Passed, body.Justification, context.RequestAborted);

            return Results.Ok(new { success = true, config_key = configKey, passed = body.Passed });
        }).RequireAuthorization();

        return app;
    }

    private static async Task<bool> IsAdminAsync(HttpContext context, IUserRepository userRepo)
    {
        var userId = GetUserId(context);
        if (string.IsNullOrEmpty(userId)) return false;
        var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
        return user is not null && user.Role == UserRole.Admin;
    }

    private static string? GetUserId(HttpContext context)
    {
        return context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }
}

public sealed record PhaseTransitionRequest(
    string TargetPhase,
    string? Justification);

public sealed record SebiOpinionRequest(
    string? DocumentRef,
    string? LawyerName,
    string? LawyerSebiReg,
    DateOnly? ReceivedDate);

public sealed record GateUpdateRequest(
    bool Passed,
    string? Justification);
