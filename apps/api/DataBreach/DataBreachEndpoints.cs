using System.Security.Claims;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

namespace SignalStack.Api.DataBreach;

public static class DataBreachEndpoints
{
    public static IEndpointRouteBuilder MapAdminBreachEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/breach");

        // ── POST /api/v1/admin/breach/record — record a detected/suspected breach ──
        // REQ-PRIVACY-006: detected or suspected breach recorded in audit_events with
        // scope (data categories, affected count, systems), detection time, discovery
        // actor, and containment steps.
        admin.MapPost("/record", async (
            HttpContext context,
            RecordBreachRequest request,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var now = DateTime.UtcNow;

            var details = new Dictionary<string, object?>
            {
                ["scope"] = new Dictionary<string, object?>
                {
                    ["data_categories"] = request.DataCategories,
                    ["affected_count"] = request.AffectedCount,
                    ["systems"] = request.Systems,
                },
                ["detection_time"] = request.DetectionTime?.ToString("o") ?? now.ToString("o"),
                ["discovery_actor"] = request.DiscoveryActor,
                ["containment_steps"] = request.ContainmentSteps,
            };

            if (request.Description is not null)
                details["description"] = request.Description;

            await auditRepo.RecordAsync(
                userId,
                "breach_suspected",
                request.DetectionTime ?? now,
                details: details,
                cancellationToken: context.RequestAborted);

            return Results.Ok(new BreachRecordedResponse(
                true, "breach_suspected", now.ToString("o")));
        }).RequireAuthorization();

        // ── POST /api/v1/admin/breach/exercise — record annual breach exercise drill ──
        // REQ-PRIVACY-006: annual test exercise recorded in audit_events.
        admin.MapPost("/exercise", async (
            HttpContext context,
            RecordExerciseRequest request,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var now = DateTime.UtcNow;

            await auditRepo.RecordAsync(
                userId,
                "breach_exercise",
                now,
                details: new Dictionary<string, object?>
                {
                    ["exercise_date"] = request.ExerciseDate?.ToString("o") ?? now.ToString("o"),
                    ["scenario"] = request.Scenario,
                    ["outcome"] = request.Outcome,
                    ["review_notes"] = request.ReviewNotes,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new BreachRecordedResponse(
                true, "breach_exercise", now.ToString("o")));
        }).RequireAuthorization();

        return app;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static string GetActorId(HttpContext context)
    {
        return context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";
    }

    private static async Task<bool> IsAdminAsync(HttpContext context, IUserRepository userRepo)
    {
        var actorId = GetActorId(context);
        if (string.IsNullOrEmpty(actorId)) return false;

        var user = await userRepo.FindByUserIdAsync(actorId, context.RequestAborted);
        return user is not null && user.Role == UserRole.Admin;
    }
}

// ── Request types ────────────────────────────────────────────────────────

public sealed record RecordBreachRequest(
    string[] DataCategories,
    int AffectedCount,
    string[] Systems,
    string DiscoveryActor,
    string[] ContainmentSteps,
    DateTime? DetectionTime = null,
    string? Description = null
);

public sealed record RecordExerciseRequest(
    string Scenario,
    string Outcome,
    string ReviewNotes,
    DateTime? ExerciseDate = null
);

// ── Response records ───────────────────────────────────────────────────────

public sealed record BreachRecordedResponse(
    bool Recorded,
    string ActionType,
    string RecordedAt
);
