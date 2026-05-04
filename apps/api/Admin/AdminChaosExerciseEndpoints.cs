using System.Security.Claims;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;

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
                return Results.BadRequest(new { error = "invalid_body" });

            if (!AllowedExerciseNumbers.Contains(body.ExerciseNumber))
                return Results.BadRequest(new { error = "invalid_exercise_number", allowed = AllowedExerciseNumbers });

            if (string.IsNullOrWhiteSpace(body.ExerciseName))
                return Results.BadRequest(new { error = "exercise_name_required" });

            if (string.IsNullOrWhiteSpace(body.ExecutionType) || !AllowedExecutionTypes.Contains(body.ExecutionType))
                return Results.BadRequest(new { error = "invalid_execution_type", allowed = AllowedExecutionTypes });

            if (string.IsNullOrWhiteSpace(body.Outcome) || !AllowedOutcomes.Contains(body.Outcome))
                return Results.BadRequest(new { error = "invalid_outcome", allowed = AllowedOutcomes });

            // REQ-SEC-011: step-up required (legal category — part of Phase C gating).
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new { error = "step_up_required", category = "legal" },
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

            return Results.Created($"/api/v1/admin/chaos-exercises/{doc.Id}", new
            {
                id = doc.Id.ToString(),
                exercise_number = doc.ExerciseNumber,
                exercise_name = doc.ExerciseName,
                execution_type = doc.ExecutionType,
                outcome = doc.Outcome,
                executed_at = doc.ExecutedAt.ToString("O"),
                findings = doc.Findings,
                remediation = doc.Remediation
            });
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
                return Results.BadRequest(new { error = "invalid_id_format" });

            var existing = await exerciseRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (existing is null)
                return Results.NotFound(new { error = "exercise_not_found" });

            var body = await context.Request.ReadFromJsonAsync<UpdateExerciseRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new { error = "invalid_body" });

            if (body.Outcome is not null && !AllowedOutcomes.Contains(body.Outcome))
                return Results.BadRequest(new { error = "invalid_outcome", allowed = AllowedOutcomes });

            // REQ-SEC-011: step-up required.
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new { error = "step_up_required", category = "legal" },
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

            return Results.Ok(new { success = true, id });
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

            var result = exercises.Select(e => new
            {
                id = e.Id.ToString(),
                exercise_number = e.ExerciseNumber,
                exercise_name = e.ExerciseName,
                execution_type = e.ExecutionType,
                outcome = e.Outcome,
                executed_at = e.ExecutedAt.ToString("O"),
                findings = e.Findings,
                remediation = e.Remediation,
                actor_id = e.ActorId,
                created_at = e.CreatedAt.ToString("O"),
                updated_at = e.UpdatedAt.ToString("O")
            }).ToList();

            return Results.Ok(new { chaos_exercises = result });
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
                return Results.BadRequest(new { error = "invalid_id_format" });

            var exercise = await exerciseRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (exercise is null)
                return Results.NotFound(new { error = "exercise_not_found" });

            return Results.Ok(new
            {
                id = exercise.Id.ToString(),
                exercise_number = exercise.ExerciseNumber,
                exercise_name = exercise.ExerciseName,
                execution_type = exercise.ExecutionType,
                outcome = exercise.Outcome,
                executed_at = exercise.ExecutedAt.ToString("O"),
                findings = exercise.Findings,
                remediation = exercise.Remediation,
                actor_id = exercise.ActorId,
                created_at = exercise.CreatedAt.ToString("O"),
                updated_at = exercise.UpdatedAt.ToString("O")
            });
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
                    new { error = "step_up_required", category = "legal" },
                    statusCode: StatusCodes.Status401Unauthorized);

            // Verify all five exercises have been passed at least once.
            var allPassed = await exerciseRepo.AllFiveExercisesPassedOnceAsync(context.RequestAborted);
            if (!allPassed)
            {
                return Results.UnprocessableEntity(new
                {
                    error = "incomplete_exercises",
                    message = "Cannot mark chaos exercises gate as complete: not all five " +
                              "exercises (1–5) have been executed with a 'pass' or 'walkthrough' outcome. " +
                              "Execute and pass each exercise before marking the gate."
                });
            }

            await gateSvc.SetGateAsync(
                userId, PhaseGateService.GateChaosExercises, true,
                "All five chaos/failure-injection exercises executed with pass/walkthrough outcome.",
                context.RequestAborted);

            return Results.Ok(new
            {
                success = true,
                gate = PhaseGateService.GateChaosExercises,
                passed = true
            });
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
