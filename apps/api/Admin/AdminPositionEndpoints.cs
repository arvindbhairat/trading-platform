using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin endpoints for manual position operations:
/// - Manual suspension (REQ-PLC-005a, REQ-ADMIN-016d)
/// - Release of ADMIN-sourced suspension (REQ-ADMIN-016d)
/// - Admin forced close (REQ-PLC-002a)
/// All mutations require step-up re-authentication (REQ-SEC-011).
/// </summary>
public static class AdminPositionEndpoints
{
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    // The positions collection stores state as strings matching PositionState enum values.
    // REQ-PLC-002: PendingEntry, Open, Suspended, Closed, Rejected
    private const string StatePendingEntry = "PendingEntry";
    private const string StateOpen = "Open";
    private const string StateSuspended = "Suspended";
    private const string StateClosed = "Closed";
    private const string StateRejected = "Rejected";

    public static IEndpointRouteBuilder MapAdminPositionEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // POST /api/v1/admin/positions/{positionId}/suspend
        // Manually suspends a single position. Requires step-up.
        // Sets state to Suspended with suspension_reason = manual_admin_suspension.
        // REQ-PLC-005a: ADMIN source (a) manual admin suspension.
        // REQ-ADMIN-016d: admin suspension action.
        admin.MapPost("/positions/{positionId}/suspend", async (
            string positionId,
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            return await ModifyPositionStateAsync(
                context, sessionRepo, auditRepo, userRepo, database,
                positionId, StateSuspended,
                reasonCode: "manual_admin_suspension",
                actionType: "position_suspended_manual");
        }).RequireAuthorization();

        // POST /api/v1/admin/positions/{positionId}/release
        // Releases an ADMIN-sourced suspension. Requires step-up.
        // Transitions from Suspended back to the position's pre-suspension state
        // (Open or PendingEntry). Only ADMIN-sourced suspensions can be released
        // through this endpoint; non-ADMIN suspensions are rejected.
        // REQ-ADMIN-016d: admin release flow.
        admin.MapPost("/positions/{positionId}/release", async (
            string positionId,
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            // Verify admin role.
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            // REQ-SEC-011: validate step-up.
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

            if (!Guid.TryParse(positionId, out var posGuid))
                return Results.BadRequest(new { error = "invalid_position_id" });

            var positions = database.GetCollection<BsonDocument>("positions");
            var ct = context.RequestAborted;

            // Read current position to verify ADMIN source and pre-suspension state.
            var current = await positions
                .Find(Builders<BsonDocument>.Filter.Eq("_id", posGuid))
                .FirstOrDefaultAsync(ct);

            if (current is null)
                return Results.NotFound(new { error = "position_not_found" });

            var currentState = current.GetValue("state", "").AsString;
            if (currentState != StateSuspended)
            {
                return Results.BadRequest(new
                {
                    error = "not_suspended",
                    message = "Only suspended positions can be released.",
                    current_state = currentState
                });
            }

            var suspensionReason = current.GetValue("suspension_reason", BsonNull.Value)?.AsString;
            var suspensionSource = current.GetValue("suspension_source", BsonNull.Value)?.AsString;

            // Only ADMIN-sourced suspensions can be released through this endpoint.
            // Non-ADMIN suspensions (e.g. LMDS circuit-break, RME_OCC, CORPORATE_ACTION)
            // have their own resolution flows (REQ-PLC-002a rows 20, 22–25).
            if (suspensionSource is not null && suspensionSource != "ADMIN")
            {
                return Results.BadRequest(new
                {
                    error = "non_admin_suspension",
                    message = $"Position was suspended by '{suspensionSource}', not ADMIN. " +
                              "Use the appropriate resolution flow for this suspension source.",
                    suspension_source = suspensionSource,
                    suspension_reason = suspensionReason
                });
            }

            // Determine the restoration state.
            // Default to Open; PendingEntry is restored if the position was
            // never filled (entry_price is zero).
            var entryPrice = current.GetValue("entry_price", BsonNull.Value);
            var restoreState = (entryPrice is BsonNull || entryPrice.IsBsonNull
                               || (entryPrice.IsDouble && entryPrice.AsDouble == 0)
                               || (entryPrice.IsDecimal128 && entryPrice.AsDecimal128 == 0))
                ? StatePendingEntry
                : StateOpen;

            var now = DateTime.UtcNow;

            // Write audit event before mutation.
            await auditRepo.RecordStepUpGatedAsync(
                userId,
                "position_suspension_released",
                now,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["position_id"] = positionId,
                    ["previous_state"] = currentState,
                    ["restored_state"] = restoreState,
                    ["suspension_reason"] = suspensionReason,
                    ["suspension_source"] = suspensionSource,
                },
                cancellationToken: ct);

            // Update position: clear suspension fields and restore state.
            var filter = Builders<BsonDocument>.Filter.Eq("_id", posGuid);
            var update = Builders<BsonDocument>.Update
                .Set("state", restoreState)
                .Unset("suspension_reason")
                .Unset("suspension_source")
                .Set("updated_at", now.ToString("o"));

            await positions.UpdateOneAsync(filter, update, cancellationToken: ct);

            return Results.Ok(new
            {
                position_id = positionId,
                previous_state = currentState,
                restored_state = restoreState,
                message = $"Position released from ADMIN suspension. Restored to {restoreState}."
            });
        }).RequireAuthorization();

        // POST /api/v1/admin/positions/{positionId}/force-close
        // Force-closes a position (step-up gated). Transitions from Open or
        // Suspended to Closed with reason admin_forced_close.
        // REQ-PLC-002a rows 19 (Open → Closed) and 27 (Suspended → Closed).
        admin.MapPost("/positions/{positionId}/force-close", async (
            string positionId,
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            return await ModifyPositionStateAsync(
                context, sessionRepo, auditRepo, userRepo, database,
                positionId, StateClosed,
                reasonCode: "admin_forced_close",
                actionType: "position_force_closed");
        }).RequireAuthorization();

        // POST /api/v1/admin/positions/{positionId}/reanchor
        // Re-anchors and releases a CORPORATE_ACTION-suspended position.
        // Step-up gated. The admin provides corrected quantity and entry price
        // reflecting the corporate action adjustment (split, bonus, rights, etc.).
        // Closes the associated rme_incidents record and transitions the position
        // to Open (or PendingEntry if the corrected entry price is zero).
        // REQ-ADMIN-016(c): corporate_action-sourced suspensions require this
        // dedicated resolution flow (no direct state-flip release).
        admin.MapPost("/positions/{positionId}/reanchor", async (
            string positionId,
            ReanchorRequest body,
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            // Verify admin role.
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            // REQ-SEC-011: validate step-up.
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

            if (!Guid.TryParse(positionId, out var posGuid))
                return Results.BadRequest(new { error = "invalid_position_id" });

            var positions = database.GetCollection<BsonDocument>("positions");
            var incidents = database.GetCollection<BsonDocument>("rme_incidents");
            var ct = context.RequestAborted;

            // Read current position.
            var current = await positions
                .Find(Builders<BsonDocument>.Filter.Eq("_id", posGuid))
                .FirstOrDefaultAsync(ct);

            if (current is null)
                return Results.NotFound(new { error = "position_not_found" });

            var currentState = current.GetValue("state", "").AsString;
            if (currentState != StateSuspended)
            {
                return Results.BadRequest(new
                {
                    error = "not_suspended",
                    message = "Only suspended positions can be re-anchored.",
                    current_state = currentState
                });
            }

            // Verify there is an open rme_incidents record for a corporate action suspension.
            var caIncident = await incidents
                .Find(Builders<BsonDocument>.Filter.And(
                    Builders<BsonDocument>.Filter.Eq("position_id", positionId),
                    Builders<BsonDocument>.Filter.Eq("incident_type", "corporate_action_suspension"),
                    Builders<BsonDocument>.Filter.Eq("status", "open")))
                .FirstOrDefaultAsync(ct);

            if (caIncident is null)
            {
                return Results.BadRequest(new
                {
                    error = "not_corporate_action_suspension",
                    message = "Position is suspended but no open corporate_action_suspension " +
                              "incident record exists. Use the standard release endpoint " +
                              "for non-CA suspensions."
                });
            }

            var now = DateTime.UtcNow;

            // Write audit event before mutation.
            await auditRepo.RecordStepUpGatedAsync(
                userId,
                "position_reanchored",
                now,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["position_id"] = positionId,
                    ["previous_state"] = currentState,
                    ["new_quantity"] = body.Quantity,
                    ["new_entry_price"] = body.EntryPrice,
                    ["justification"] = body.Justification,
                },
                cancellationToken: ct);

            // Determine restoration state.
            var restoreState = body.EntryPrice == 0m ? StatePendingEntry : StateOpen;

            // Update position: apply re-anchored values and release from suspension.
            var positionFilter = Builders<BsonDocument>.Filter.Eq("_id", posGuid);
            var positionUpdate = Builders<BsonDocument>.Update
                .Set("state", restoreState)
                .Set("quantity", (double)body.Quantity)
                .Set("entry_price", (double)body.EntryPrice)
                .Set("updated_at", now.ToString("o"));

            await positions.UpdateOneAsync(positionFilter, positionUpdate, cancellationToken: ct);

            // Close the incident record.
            var incidentFilter = Builders<BsonDocument>.Filter.Eq("_id", caIncident["_id"]);
            var incidentUpdate = Builders<BsonDocument>.Update
                .Set("status", "resolved")
                .Set("resolved_at", now.ToString("o"))
                .Set("resolved_by", userId)
                .Set("updated_at", now.ToString("o"));

            await incidents.UpdateOneAsync(incidentFilter, incidentUpdate, cancellationToken: ct);

            return Results.Ok(new
            {
                position_id = positionId,
                previous_state = currentState,
                restored_state = restoreState,
                quantity = body.Quantity,
                entry_price = body.EntryPrice,
                message = $"Position re-anchored and released from CA suspension. " +
                          $"Restored to {restoreState} with qty={body.Quantity}, entryPrice={body.EntryPrice}."
            });
        }).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Request body for the re-anchor endpoint.
    /// </summary>
    private sealed record ReanchorRequest(
        decimal Quantity,
        decimal EntryPrice,
        string? Justification = null);

    private static async Task<IResult> ModifyPositionStateAsync(
        HttpContext context,
        ISessionRepository sessionRepo,
        IAuditEventRepository auditRepo,
        IUserRepository userRepo,
        IMongoDatabase database,
        string positionId,
        string targetState,
        string? reasonCode,
        string actionType)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

        // Verify admin role.
        var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
        if (user is null || user.Role != UserRole.Admin)
            return Results.Forbid();

        // REQ-SEC-011: validate step-up.
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

        if (!Guid.TryParse(positionId, out var posGuid))
            return Results.BadRequest(new { error = "invalid_position_id" });

        var positions = database.GetCollection<BsonDocument>("positions");
        var ct = context.RequestAborted;

        // Read current position.
        var current = await positions
            .Find(Builders<BsonDocument>.Filter.Eq("_id", posGuid))
            .FirstOrDefaultAsync(ct);

        if (current is null)
            return Results.NotFound(new { error = "position_not_found" });

        var currentState = current.GetValue("state", "").AsString;

        // Validate that the current state allows the target transition.
        if (targetState == StateSuspended)
        {
            // Can suspend from PendingEntry or Open.
            if (currentState != StatePendingEntry && currentState != StateOpen)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_transition",
                    message = $"Cannot suspend position in state '{currentState}'. " +
                              "Only PendingEntry and Open positions can be suspended.",
                    current_state = currentState
                });
            }
        }
        else if (targetState == StateClosed)
        {
            // Can force-close from Open or Suspended.
            if (currentState != StateOpen && currentState != StateSuspended)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_transition",
                    message = $"Cannot force-close position in state '{currentState}'. " +
                              "Only Open and Suspended positions can be force-closed.",
                    current_state = currentState
                });
            }
        }

        var now = DateTime.UtcNow;

        // Write audit event before mutation.
        await auditRepo.RecordStepUpGatedAsync(
            userId,
            actionType,
            now,
            session!.StepUpEventId!.Value,
            details: new Dictionary<string, object?>
            {
                ["position_id"] = positionId,
                ["previous_state"] = currentState,
                ["target_state"] = targetState,
                ["reason_code"] = reasonCode,
            },
            cancellationToken: ct);

        // Build update: set state and optional suspension tracking fields.
        var filter = Builders<BsonDocument>.Filter.Eq("_id", posGuid);
        var update = Builders<BsonDocument>.Update
            .Set("state", targetState)
            .Set("updated_at", now.ToString("o"));

        if (targetState == StateClosed)
        {
            update = update.Set("closed_at", now.ToString("o"));
        }

        if (targetState == StateSuspended && reasonCode is not null)
        {
            update = update
                .Set("suspension_reason", reasonCode)
                .Set("suspension_source", "ADMIN");
        }

        await positions.UpdateOneAsync(filter, update, cancellationToken: ct);

        return Results.Ok(new
        {
            position_id = positionId,
            previous_state = currentState,
            new_state = targetState,
            action = actionType,
            reason_code = reasonCode,
        });
    }
}
