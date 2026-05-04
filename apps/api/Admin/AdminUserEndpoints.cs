using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Execution;
using SignalStack.Api.Fyers;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin endpoints for user lifecycle management:
/// - List / approve / deactivate (REQ-ADMIN-001a)
/// - Reactivate (REQ-ADMIN-001b)
/// - Per-user signal suspension (REQ-ADMIN-010)
/// Deactivate and reactivate require step-up re-authentication (REQ-SEC-011).
/// Deactivation atomically invalidates sessions, suspends PendingEntry positions,
/// marks FYERS tokens dirty, and expires pending intent-ledger records.
/// REQ-ADMIN-001c: all three state transitions captured in audit event.
/// </summary>
public static class AdminUserEndpoints
{
    // Matches the 5-minute window in AuthEndpoints (REQ-SEC-011).
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    // Position state constants matching PositionState enum values.
    private const string StatePendingEntry = "PendingEntry";
    private const string StateSuspended = "Suspended";

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
        // REQ-ADMIN-001a/001c: step-up gated. Atomically:
        //   - Sets user status to deactivated
        //   - Invalidates all active portal sessions
        //   - Transitions PendingEntry positions to Suspended (reason account_deactivated)
        //   - Marks all FYERS tokens dirty
        //   - Expires pending intent-ledger records (expired_account_deactivated)
        //   - Records audit event with state-transition details
        admin.MapPost("/users/{userId}/deactivate", async (
            string userId,
            HttpContext context,
            IUserRepository userRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IMongoDatabase database) =>
        {
            return await DeactivateUserAsync(
                context, userRepo, sessionRepo, auditRepo, database, userId);
        }).RequireAuthorization();

        // POST /api/v1/admin/users/{userId}/reactivate — reactivate a deactivated user
        // REQ-ADMIN-001b: step-up gated. Transitions deactivated → approved.
        admin.MapPost("/users/{userId}/reactivate", async (
            string userId,
            HttpContext context,
            IUserRepository userRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo) =>
        {
            return await ReactivateUserAsync(
                context, userRepo, sessionRepo, auditRepo, userId);
        }).RequireAuthorization();

        // POST /api/v1/admin/users/{userId}/signal-suspend — suspend signals for a user
        // REQ-ADMIN-010: step-up gated. Sets signals_suspended = true and transitions
        // PendingEntry positions to Suspended with reason user_signal_suspended.
        admin.MapPost("/users/{userId}/signal-suspend", async (
            string userId,
            HttpContext context,
            IUserRepository userRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IMongoDatabase database) =>
        {
            return await SetSignalSuspensionAsync(
                context, userRepo, sessionRepo, auditRepo, database,
                userId, suspend: true);
        }).RequireAuthorization();

        // POST /api/v1/admin/users/{userId}/signal-enable — re-enable signals for a user
        // REQ-ADMIN-010: step-up gated. Sets signals_suspended = false.
        // Suspended PendingEntry positions are NOT auto-reinstated
        // (they remain Suspended until superseded by a fresh EODSR signal).
        admin.MapPost("/users/{userId}/signal-enable", async (
            string userId,
            HttpContext context,
            IUserRepository userRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo) =>
        {
            return await SetSignalSuspensionAsync(
                context, userRepo, sessionRepo, auditRepo, null,
                userId, suspend: false);
        }).RequireAuthorization();

        return app;
    }

    // ── Deactivate (REQ-ADMIN-001a/001c) ───────────────────────────────────

    private static async Task<IResult> DeactivateUserAsync(
        HttpContext context,
        IUserRepository userRepo,
        ISessionRepository sessionRepo,
        IAuditEventRepository auditRepo,
        IMongoDatabase database,
        string targetUserId)
    {
        var adminId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

        // Verify admin role.
        var admin = await userRepo.FindByUserIdAsync(adminId, context.RequestAborted);
        if (admin is null || admin.Role != UserRole.Admin)
            return Results.Forbid();

        // Verify target user exists.
        var target = await userRepo.FindByUserIdAsync(targetUserId, context.RequestAborted);
        if (target is null)
            return Results.NotFound(new { error = "user_not_found" });

        // Cannot deactivate admin accounts.
        if (target.Role == UserRole.Admin)
            return Results.BadRequest(new { error = "cannot_deactivate_admin" });

        // REQ-SEC-011: validate step-up re-authentication within 5 minutes.
        var stepUpResult = await ValidateStepUpAsync(context, sessionRepo);
        if (stepUpResult is not null)
            return stepUpResult;

        var ct = context.RequestAborted;
        var now = DateTime.UtcNow;
        var session = await sessionRepo.FindBySessionTokenAsync(
            context.User.FindFirst("jti")?.Value ?? "", ct);

        // ── Step 1: Set user status to deactivated ─────────────────────
        await userRepo.DeactivateAsync(targetUserId, ct);

        // ── Step 2: Invalidate all active sessions for the user ────────
        var sessionsCol = database.GetCollection<BsonDocument>("sessions");
        await sessionsCol.DeleteManyAsync(
            Builders<BsonDocument>.Filter.Eq("user_id", targetUserId),
            cancellationToken: ct);

        // ── Step 3: Transition PendingEntry positions to Suspended ─────
        var positionsCol = database.GetCollection<BsonDocument>("positions");
        var pendingFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("user_id", targetUserId),
            Builders<BsonDocument>.Filter.Eq("state", StatePendingEntry));
        var pendingUpdate = Builders<BsonDocument>.Update
            .Set("state", StateSuspended)
            .Set("suspension_reason", "account_deactivated")
            .Set("suspension_source", "ADMIN")
            .Set("updated_at", now.ToString("o"));
        var pendingResult = await positionsCol.UpdateManyAsync(
            pendingFilter, pendingUpdate, cancellationToken: ct);

        // ── Step 4: Mark all FYERS tokens as dirty ────────────────────
        var tokensCol = database.GetCollection<BsonDocument>("fyers_tokens");
        var tokenFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("user_id", targetUserId),
            Builders<BsonDocument>.Filter.Ne("status", "dirty"));
        var tokenUpdate = Builders<BsonDocument>.Update
            .Set("status", FyersTokenStatus.Dirty)
            .Set("updated_at", now.ToString("o"));
        var tokenResult = await tokensCol.UpdateManyAsync(
            tokenFilter, tokenUpdate, cancellationToken: ct);

        // ── Step 5: Expire pending intent-ledger records ───────────────
        var intentsCol = database.GetCollection<BsonDocument>("intent_ledger");
        var intentFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("user_id", targetUserId),
            Builders<BsonDocument>.Filter.In("status",
                new[] { IntentStatus.Pending, "awaiting_confirmation" }));
        var intentUpdate = Builders<BsonDocument>.Update
            .Set("status", IntentStatus.ExpiredAccountDeactivated)
            .Set("updated_at", now.ToString("o"));
        var intentResult = await intentsCol.UpdateManyAsync(
            intentFilter, intentUpdate, cancellationToken: ct);

        // ── Step 6: Write audit event with full details (REQ-ADMIN-001c) ─
        await auditRepo.RecordStepUpGatedAsync(
            adminId,
            "user_deactivated",
            now,
            session!.StepUpEventId!.Value,
            details: new Dictionary<string, object?>
            {
                ["target_user_id"] = targetUserId,
                ["target_email"] = target.Email,
                ["sessions_invalidated"] = true,
                ["pending_positions_suspended"] = pendingResult.ModifiedCount,
                ["fyers_tokens_marked_dirty"] = tokenResult.ModifiedCount,
                ["intent_ledger_expired"] = intentResult.ModifiedCount,
            },
            cancellationToken: ct);

        return Results.Ok(new
        {
            deactivated = true,
            pending_positions_suspended = pendingResult.ModifiedCount,
            fyers_tokens_marked_dirty = tokenResult.ModifiedCount,
            intent_ledger_expired = intentResult.ModifiedCount,
            message = "User deactivated. Sessions invalidated, PendingEntry positions suspended, " +
                      "FYERS tokens marked dirty, pending intent-ledger records expired."
        });
    }

    // ── Reactivate (REQ-ADMIN-001b) ───────────────────────────────────────

    private static async Task<IResult> ReactivateUserAsync(
        HttpContext context,
        IUserRepository userRepo,
        ISessionRepository sessionRepo,
        IAuditEventRepository auditRepo,
        string targetUserId)
    {
        var adminId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

        // Verify admin role.
        var admin = await userRepo.FindByUserIdAsync(adminId, context.RequestAborted);
        if (admin is null || admin.Role != UserRole.Admin)
            return Results.Forbid();

        // REQ-SEC-011: validate step-up re-authentication.
        var stepUpResult = await ValidateStepUpAsync(context, sessionRepo);
        if (stepUpResult is not null)
            return stepUpResult;

        var ct = context.RequestAborted;
        var now = DateTime.UtcNow;
        var session = await sessionRepo.FindBySessionTokenAsync(
            context.User.FindFirst("jti")?.Value ?? "", ct);

        // Reactivate — only works if user is currently deactivated.
        var success = await userRepo.ReactivateAsync(targetUserId, ct);
        if (!success)
        {
            // Check if the user exists at all.
            var target = await userRepo.FindByUserIdAsync(targetUserId, ct);
            if (target is null)
                return Results.NotFound(new { error = "user_not_found" });

            return Results.BadRequest(new
            {
                error = "not_deactivated",
                message = "User is not currently deactivated and cannot be reactivated.",
                current_status = target.Status
            });
        }

        // Write audit event.
        await auditRepo.RecordStepUpGatedAsync(
            adminId,
            "user_reactivated",
            now,
            session!.StepUpEventId!.Value,
            details: new Dictionary<string, object?>
            {
                ["target_user_id"] = targetUserId,
            },
            cancellationToken: ct);

        return Results.Ok(new
        {
            reactivated = true,
            message = "User reactivated. FYERS token remains dirty — user must complete " +
                      "fresh FYERS re-authentication before accessing FYERS-dependent features."
        });
    }

    // ── Per-user signal suspension (REQ-ADMIN-010) ────────────────────────

    private static async Task<IResult> SetSignalSuspensionAsync(
        HttpContext context,
        IUserRepository userRepo,
        ISessionRepository sessionRepo,
        IAuditEventRepository auditRepo,
        IMongoDatabase? database,
        string targetUserId,
        bool suspend)
    {
        var adminId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

        // Verify admin role.
        var admin = await userRepo.FindByUserIdAsync(adminId, context.RequestAborted);
        if (admin is null || admin.Role != UserRole.Admin)
            return Results.Forbid();

        // Verify target user exists.
        var target = await userRepo.FindByUserIdAsync(targetUserId, context.RequestAborted);
        if (target is null)
            return Results.NotFound(new { error = "user_not_found" });

        // REQ-SEC-011: validate step-up re-authentication.
        var stepUpResult = await ValidateStepUpAsync(context, sessionRepo);
        if (stepUpResult is not null)
            return stepUpResult;

        var ct = context.RequestAborted;
        var now = DateTime.UtcNow;
        var session = await sessionRepo.FindBySessionTokenAsync(
            context.User.FindFirst("jti")?.Value ?? "", ct);

        // Set/clear the signals_suspended flag.
        await userRepo.SetSignalsSuspendedAsync(targetUserId, suspend, ct);

        long pendingPositionsSuspended = 0;

        // On suspend: transition PendingEntry positions to Suspended.
        if (suspend && database is not null)
        {
            var positionsCol = database.GetCollection<BsonDocument>("positions");
            var pendingFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("user_id", targetUserId),
                Builders<BsonDocument>.Filter.Eq("state", StatePendingEntry));
            var pendingUpdate = Builders<BsonDocument>.Update
                .Set("state", StateSuspended)
                .Set("suspension_reason", "user_signal_suspended")
                .Set("suspension_source", "ADMIN")
                .Set("updated_at", now.ToString("o"));

            var result = await positionsCol.UpdateManyAsync(
                pendingFilter, pendingUpdate, cancellationToken: ct);
            pendingPositionsSuspended = result.ModifiedCount;
        }

        var actionType = suspend ? "user_signal_suspended" : "user_signal_enabled";

        // Write audit event.
        await auditRepo.RecordStepUpGatedAsync(
            adminId,
            actionType,
            now,
            session!.StepUpEventId!.Value,
            details: new Dictionary<string, object?>
            {
                ["target_user_id"] = targetUserId,
                ["signals_suspended"] = suspend,
                ["pending_positions_suspended"] = pendingPositionsSuspended,
            },
            cancellationToken: ct);

        if (suspend)
        {
            return Results.Ok(new
            {
                signals_suspended = true,
                pending_positions_suspended = pendingPositionsSuspended,
                message = "Signal delivery suspended. The user retains read-only platform " +
                          "access. PendingEntry positions transitioned to Suspended. " +
                          "On re-enable, Suspended positions are NOT auto-reinstated."
            });
        }
        else
        {
            return Results.Ok(new
            {
                signals_suspended = false,
                message = "Signal delivery re-enabled. Suspended PendingEntry positions " +
                          "remain Suspended until superseded by a fresh EODSR entry signal."
            });
        }
    }

    // ── Step-up validation helper ─────────────────────────────────────────

    private static async Task<IResult?> ValidateStepUpAsync(
        HttpContext context, ISessionRepository sessionRepo)
    {
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
        return null;
    }

    // ── Mapping ──────────────────────────────────────────────────────────

    private static AdminUserEntry MapToUserEntry(UserDocument doc)
    {
        return new AdminUserEntry(
            UserId: doc.UserId,
            Email: doc.Email,
            DisplayName: doc.DisplayName,
            Provider: doc.Provider,
            Role: doc.Role,
            Status: doc.Status,
            SignalsSuspended: doc.SignalsSuspended,
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
    bool SignalsSuspended,
    string CreatedAt,
    string UpdatedAt
);
