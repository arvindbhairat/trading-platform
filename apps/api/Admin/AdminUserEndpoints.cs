using System.Security.Claims;
using SignalStack.Api.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;

namespace SignalStack.Api.Admin;

public static class AdminUserEndpoints
{
    // Matches the 5-minute window in AuthEndpoints (REQ-SEC-011).
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapAdminUserEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/users — list all users with optional ?status= filter
        // REQ-ROLE-004: admin can view all users and their approval states.
        admin.MapGet("/users", async (
            HttpContext context,
            IUserRepository userRepo,
            string? status) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var users = await userRepo.ListUsersAsync(status, context.RequestAborted);
            var result = users.Select(MapToUserEntry).ToList();

            return Results.Ok(new { users = result });
        }).RequireAuthorization();

        // GET /api/v1/admin/users/pending/count — count of pending-approval users
        // Used by the admin nav badge to show a live count.
        admin.MapGet("/users/pending/count", async (
            HttpContext context,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var count = await userRepo.CountPendingAsync(context.RequestAborted);
            return Results.Ok(new { count });
        }).RequireAuthorization();

        // POST /api/v1/admin/users/{userId}/approve — approve a user
        // REQ-ROLE-004: admin transitions user from pending_approval → approved.
        // REQ-LEGAL-003: Phase A ceiling enforced by UserApprovalService.
        // Audit event recorded before mutation (REQ-CONFIG-005a pattern).
        admin.MapPost("/users/{userId}/approve", async (
            string userId,
            HttpContext context,
            UserApprovalService approvalSvc,
            IAuditEventRepository auditRepo) =>
        {
            var adminId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            var result = await approvalSvc.ApproveAsync(userId, context.RequestAborted);

            if (!result.Success)
            {
                return result.Error == "tester_ceiling_reached"
                    ? Results.UnprocessableEntity(new
                    {
                        error = result.Error,
                        ceiling = result.Ceiling,
                        current_count = result.CurrentCount
                    })
                    : Results.NotFound(new { error = result.Error });
            }

            // Write audit event for approval (REQ-CONFIG-005a pattern: audit before effect).
            await auditRepo.RecordAsync(
                adminId,
                "user_approved",
                DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["target_user_id"] = userId,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new { approved = true });
        }).RequireAuthorization();

        // POST /api/v1/admin/users/{userId}/deactivate — deactivate a user
        // REQ-ROLE-004: admin transitions any user to deactivated state.
        // Audit event recorded before mutation (REQ-CONFIG-005a pattern).
        admin.MapPost("/users/{userId}/deactivate", async (
            string userId,
            HttpContext context,
            UserApprovalService approvalSvc,
            IAuditEventRepository auditRepo) =>
        {
            var adminId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            await approvalSvc.DeactivateAsync(userId, context.RequestAborted);

            // Write audit event for deactivation.
            await auditRepo.RecordAsync(
                adminId,
                "user_deactivated",
                DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["target_user_id"] = userId,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new { deactivated = true });
        }).RequireAuthorization();

        return app;
    }

    private static AdminUserEntry MapToUserEntry(UserDocument doc)
    {
        return new AdminUserEntry(
            UserId: doc.UserId,
            Email: doc.Email,
            DisplayName: doc.DisplayName,
            Provider: doc.Provider,
            Role: doc.Role,
            Status: doc.Status,
            CreatedAt: doc.CreatedAt.ToString("o"),
            UpdatedAt: doc.UpdatedAt.ToString("o")
        );
    }
}

public sealed record AdminUserEntry(
    string UserId,
    string Email,
    string DisplayName,
    string Provider,
    string Role,
    string Status,
    string CreatedAt,
    string UpdatedAt
);
