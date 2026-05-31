using System.Security.Claims;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Domain.Admin;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using SignalStack.Storage.Admin;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin REST endpoints for chaos / failure-injection exercise recording.
/// P8-T12 — REQ-NFR-014.
/// </summary>
public static class AdminChaosExerciseEndpoints
{
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    private static readonly int[] AllowedExerciseNumbers = [1, 2, 3, 4, 5];
    private static readonly string[] AllowedExecutionTypes = ["walkthrough", "simulation", "live"];
    private static readonly string[] AllowedOutcomes = ["pass", "fail", "partial", "walkthrough"];

    public static IEndpointRouteBuilder MapAdminChaosExerciseEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // ────────────────────────────────────────────────────────────────────
        // POST /api/v1/admin/chaos-exercises
        // Record a new chaos / failure-injection exercise execution.
        // P8-T12 / REQ-NFR-014.
        admin.MapPost("/chaos-exercises", async (
            HttpContext context,
            IChaosExerciseRepository exerciseRepo,
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

            var body = await context.Request.ReadFromJsonAsync<RecordExerciseRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new AdminErrorResponse("invalid_body"));

            if (!AllowedExerciseNumbers.Contains(body.ExerciseNumber))
                return Results.BadRequest(new AdminAllowedValuesError("invalid_exercise_number", AllowedExerciseNumbers));

            if (string.IsNullOrWhiteSpace(body.ExerciseName))
                return Results.BadRequest(new AdminErrorResponse("exercise_name_required"));

            if (string.IsNullOrWhiteSpace(body.ExecutionType) || !AllowedExecutionTypes.Contains(body.ExecutionType))
                return Results.BadRequest(new AdminAllowedValuesError("invalid_execution_type", AllowedExecutionTypes));

            if (string.IsNullOrWhiteSpace(body.Outcome) || !AllowedOutcomes.Contains(body.Outcome))
                return Results.BadRequest(new AdminAllowedValuesError("invalid_outcome", AllowedOutcomes));

            // REQ-SEC-011: step-up required (legal category — part of Phase C gating).
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new AdminStepUpRequiredResponse("step_up_required", "legal"),
                    statusCode: StatusCodes.Status401Unauthorized);

            var now = DateTime.UtcNow;
            var doc = new ChaosExerciseDocument
            {
                Id = ObjectId.GenerateNewId(),
                ExerciseNumber = body.ExerciseNumber,
                ExerciseName = body.ExerciseName,
                ExecutionType = body.ExecutionType,
                Outcome = body.Outcome,
                ExecutedAt = body.ExecutedAt ?? now,
                Findings = body.Findings,
                Remediation = body.Remediation,
                ActorId = userId,
                CreatedAt = now,
                UpdatedAt = now
            };

            await exerciseRepo.CreateAsync(doc, context.RequestAborted);

            await auditRepo.RecordStepUpGatedAsync(
                userId, "chaos_exercise_executed", now,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["exercise_id"] = doc.Id.ToString(),
                    ["exercise_number"] = doc.ExerciseNumber,
                    ["exercise_name"] = doc.ExerciseName,
                    ["execution_type"] = doc.ExecutionType,
                    ["outcome"] = doc.Outcome
                },
                cancellationToken: context.RequestAborted);

            return Results.Created($"/api/v1/admin/chaos-exercises/{doc.Id}",
                new ChaosExerciseResponse(
                    doc.Id.ToString(),
                    doc.ExerciseNumber,
                    doc.ExerciseName,
                    doc.ExecutionType,
                    doc.Outcome,
                    doc.ExecutedAt.ToString("O"),
                    doc.Findings,
                    doc.Remediation));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // PATCH /api/v1/admin/chaos-exercises/{id}
        // Update an exercise execution record (outcome, findings, remediation).
        // P8-T12 / REQ-NFR-014.
        admin.MapPatch("/chaos-exercises/{id}", async (
            string id,
            HttpContext context,
            IChaosExerciseRepository exerciseRepo,
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

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new AdminErrorResponse("invalid_id_format"));

            var existing = await exerciseRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (existing is null)
                return Results.NotFound(new AdminErrorResponse("exercise_not_found"));

            var body = await context.Request.ReadFromJsonAsync<UpdateExerciseRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new AdminErrorResponse("invalid_body"));

            if (body.Outcome is not null && !AllowedOutcomes.Contains(body.Outcome))
                return Results.BadRequest(new AdminAllowedValuesError("invalid_outcome", AllowedOutcomes));

            // REQ-SEC-011: step-up required.
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new AdminStepUpRequiredResponse("step_up_required", "legal"),
                    statusCode: StatusCodes.Status401Unauthorized);

            await exerciseRepo.UpdateAsync(
                objectId, body.Outcome, body.Findings, body.Remediation, context.RequestAborted);

            await auditRepo.RecordStepUpGatedAsync(
                userId, "chaos_exercise_updated", DateTime.UtcNow,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["exercise_id"] = id,
                    ["exercise_number"] = existing.ExerciseNumber,
                    ["previous_outcome"] = existing.Outcome,
                    ["new_outcome"] = body.Outcome,
                    ["findings"] = body.Findings,
                    ["remediation"] = body.Remediation
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new ChaosExerciseUpdatedResponse(true, id));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // GET /api/v1/admin/chaos-exercises
        // List all recorded chaos exercise executions.
        // P8-T12 / REQ-NFR-014.
        admin.MapGet("/chaos-exercises", async (
            HttpContext context,
            IChaosExerciseRepository exerciseRepo,
            IUserRepository userRepo) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var exercises = await exerciseRepo.ListAllAsync(context.RequestAborted);

            var result = exercises.Select(e => new ChaosExerciseDetailResponse(
                e.Id.ToString(),
                e.ExerciseNumber,
                e.ExerciseName,
                e.ExecutionType,
                e.Outcome,
                e.ExecutedAt.ToString("O"),
                e.Findings,
                e.Remediation,
                e.ActorId,
                e.CreatedAt.ToString("O"),
                e.UpdatedAt.ToString("O")
            )).ToList();

            return Results.Ok(new ChaosExerciseListResponse(result));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // GET /api/v1/admin/chaos-exercises/{id}
        // Get a single exercise execution record.
        admin.MapGet("/chaos-exercises/{id}", async (
            string id,
            HttpContext context,
            IChaosExerciseRepository exerciseRepo,
            IUserRepository userRepo) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new AdminErrorResponse("invalid_id_format"));

            var exercise = await exerciseRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (exercise is null)
                return Results.NotFound(new AdminErrorResponse("exercise_not_found"));

            return Results.Ok(new ChaosExerciseDetailResponse(
                exercise.Id.ToString(),
                exercise.ExerciseNumber,
                exercise.ExerciseName,
                exercise.ExecutionType,
                exercise.Outcome,
                exercise.ExecutedAt.ToString("O"),
                exercise.Findings,
                exercise.Remediation,
                exercise.ActorId,
                exercise.CreatedAt.ToString("O"),
                exercise.UpdatedAt.ToString("O")
            ));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // POST /api/v1/admin/chaos-exercises/mark-gate
        // Mark the chaos exercises Phase C gate as satisfied.
        // Only permitted when all five exercises have been passed at least once.
        admin.MapPost("/chaos-exercises/mark-gate", async (
            HttpContext context,
            IChaosExerciseRepository exerciseRepo,
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

            // REQ-SEC-011: step-up required.
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new AdminStepUpRequiredResponse("step_up_required", "legal"),
                    statusCode: StatusCodes.Status401Unauthorized);

            // Verify all five exercises have been passed at least once.
            var allPassed = await exerciseRepo.AllFiveExercisesPassedOnceAsync(context.RequestAborted);
            if (!allPassed)
            {
                return Results.UnprocessableEntity(new AdminErrorResponse(
                    "incomplete_exercises",
                    "Cannot mark chaos exercises gate as complete: not all five " +
                    "exercises (1–5) have been executed with a 'pass' or 'walkthrough' outcome. " +
                    "Execute and pass each exercise before marking the gate."));
            }

            await gateSvc.SetGateAsync(
                userId, PhaseGateService.GateChaosExercises, true,
                "All five chaos/failure-injection exercises executed with pass/walkthrough outcome.",
                context.RequestAborted);

            return Results.Ok(new ChaosExerciseGateResponse(
                true, PhaseGateService.GateChaosExercises, true));
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

// ── Request DTOs ────────────────────────────────────────────────────────────

public sealed record RecordExerciseRequest(
    int ExerciseNumber,
    string? ExerciseName,
    string? ExecutionType,
    string? Outcome,
    DateTime? ExecutedAt,
    string? Findings,
    string? Remediation);

public sealed record UpdateExerciseRequest(
    string? Outcome,
    string? Findings,
    string? Remediation);

// ── Response DTOs ────────────────────────────────────────────────────────────

public sealed record ChaosExerciseResponse(
    string Id,
    int ExerciseNumber,
    string? ExerciseName,
    string? ExecutionType,
    string? Outcome,
    string ExecutedAt,
    string? Findings,
    string? Remediation);

public sealed record ChaosExerciseDetailResponse(
    string Id,
    int ExerciseNumber,
    string? ExerciseName,
    string? ExecutionType,
    string? Outcome,
    string ExecutedAt,
    string? Findings,
    string? Remediation,
    string? ActorId,
    string CreatedAt,
    string UpdatedAt);

public sealed record ChaosExerciseListResponse(IEnumerable<ChaosExerciseDetailResponse> ChaosExercises);

public sealed record ChaosExerciseGateResponse(bool Success, string Gate, bool Passed);

public sealed record ChaosExerciseUpdatedResponse(bool Success, string Id);

