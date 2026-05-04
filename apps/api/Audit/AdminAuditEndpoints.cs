using System.Security.Claims;
using MongoDB.Bson;
using SignalStack.Api.Auth;
using SignalStack.Api.Users;

namespace SignalStack.Api.Audit;

/// <summary>
/// Admin REST endpoints for querying the immutable audit event log.
/// P8-T1 — helper APIs + retention.
/// </summary>
public static class AdminAuditEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuditEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/audit-events — paginated list with optional filters
        // P8-T1: admin helper API for querying audit events.
        // Filters: ?actor_id=, ?action_type=, ?from=, ?to=, ?page=, ?page_size=
        admin.MapGet("/audit-events", async (
            HttpContext context,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            string? actor_id,
            string? action_type,
            DateTime? from,
            DateTime? to,
            int page = 1,
            int page_size = 50) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            // Clamp pagination.
            if (page < 1) page = 1;
            if (page_size < 1) page_size = 50;
            if (page_size > 200) page_size = 200;

            var filter = new AuditEventFilter(
                ActorId: actor_id,
                ActionType: action_type,
                From: from,
                To: to);

            var (items, totalCount) = await auditRepo.ListAsync(
                filter, page, page_size, context.RequestAborted);

            var entries = items.Select(MapToEntry).ToList();

            return Results.Ok(new
            {
                entries,
                total_count = totalCount,
                page,
                page_size,
                total_pages = totalCount > 0
                    ? (int)Math.Ceiling((double)totalCount / page_size)
                    : 0
            });
        }).RequireAuthorization();

        // GET /api/v1/admin/audit-events/{id} — single event detail
        admin.MapGet("/audit-events/{id}", async (
            string id,
            HttpContext context,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new { error = "invalid_id_format" });

            var doc = await auditRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (doc is null)
                return Results.NotFound(new { error = "audit_event_not_found" });

            return Results.Ok(new { entry = MapToEntry(doc) });
        }).RequireAuthorization();

        return app;
    }

    private static async Task<bool> IsAdminAsync(HttpContext context, IUserRepository userRepo)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

        if (string.IsNullOrEmpty(userId))
            return false;

        var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
        return user is not null && user.Role == UserRole.Admin;
    }

    private static object MapToEntry(AuditEventDocument doc)
    {
        return new
        {
            id = doc.Id.ToString(),
            actor_id = doc.ActorId,
            action_type = doc.ActionType,
            event_at = doc.EventAt.ToString("O"),
            details = doc.Details,
            step_up_event_ref = doc.StepUpEventRef?.ToString()
        };
    }
}
