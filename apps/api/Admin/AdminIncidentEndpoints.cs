using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin endpoints for the <c>rme_incidents</c> dashboard: listing, acknowledging,
/// and resolving RME incidents. Resolution of <c>concurrency_conflict_unresolved</c>
/// incidents also transitions the associated position from Suspended back to Open
/// or PendingEntry (frozen-with-visibility resolution, REQ-RME-CONC-007).
/// All mutations require step-up re-authentication (REQ-SEC-011).
/// </summary>
public static class AdminIncidentEndpoints
{
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapAdminIncidentEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/incidents
        // Lists rme_incidents with optional status filter and pagination.
        // No step-up required for read-only queries.
        admin.MapGet("/incidents", async (
            string? status,
            int? limit,
            int? skip,
            IMongoDatabase database) =>
        {
            var incidents = database.GetCollection<BsonDocument>("rme_incidents");
            var filter = Builders<BsonDocument>.Filter.Empty;

            if (!string.IsNullOrEmpty(status))
            {
                filter = Builders<BsonDocument>.Filter.Eq("status", status);
            }

            var take = Math.Clamp(limit ?? 50, 1, 200);
            var offset = Math.Max(skip ?? 0, 0);

            var results = await incidents
                .Find(filter)
                .SortByDescending(d => d["created_at"])
                .Skip(offset)
                .Limit(take)
                .ToListAsync();

            var totalCount = await incidents.CountDocumentsAsync(filter);

            return Results.Ok(new
            {
                incidents = results.Select(MapIncident),
                total_count = totalCount,
                limit = take,
                skip = offset,
            });
        }).RequireAuthorization();

        // GET /api/v1/admin/incidents/{id}
        // Returns a single incident by ObjectId.
        admin.MapGet("/incidents/{id}", async (
            string id,
            IMongoDatabase database) =>
        {
            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new { error = "invalid_incident_id" });

            var incidents = database.GetCollection<BsonDocument>("rme_incidents");
            var incident = await incidents
                .Find(Builders<BsonDocument>.Filter.Eq("_id", objectId))
                .FirstOrDefaultAsync();

            if (incident is null)
                return Results.NotFound(new { error = "incident_not_found" });

            return Results.Ok(MapIncident(incident));
        }).RequireAuthorization();

        // PUT /api/v1/admin/incidents/{id}/acknowledge
        // Acknowledges an open incident. Step-up required.
        // Transitions status from "open" to "acknowledged".
        admin.MapPut("/incidents/{id}/acknowledge", async (
            string id,
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            var result = await VerifyAdminAndStepUp(context, sessionRepo, userRepo);
            if (result is not null) return result;

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new { error = "invalid_incident_id" });

            var incidents = database.GetCollection<BsonDocument>("rme_incidents");
            var ct = context.RequestAborted;

            var incident = await incidents
                .Find(Builders<BsonDocument>.Filter.Eq("_id", objectId))
                .FirstOrDefaultAsync(ct);

            if (incident is null)
                return Results.NotFound(new { error = "incident_not_found" });

            var currentStatus = incident.GetValue("status", "").AsString;
            if (currentStatus != "open")
            {
                return Results.BadRequest(new
                {
                    error = "invalid_status",
                    message = $"Only open incidents can be acknowledged. Current status: {currentStatus}",
                    current_status = currentStatus,
                });
            }

            var now = DateTime.UtcNow;

            // Write audit event before mutation.
            var userId = GetUserId(context);
            await auditRepo.RecordAsync(
                userId,
                "incident_acknowledged",
                now,
                details: new Dictionary<string, object?>
                {
                    ["incident_id"] = id,
                    ["incident_type"] = incident.GetValue("incident_type", BsonNull.Value)?.AsString,
                    ["position_id"] = incident.GetValue("position_id", BsonNull.Value)?.AsString,
                },
                cancellationToken: ct);

            // Update status to acknowledged.
            var filter = Builders<BsonDocument>.Filter.Eq("_id", objectId)
                & Builders<BsonDocument>.Filter.Eq("status", "open");
            var update = Builders<BsonDocument>.Update
                .Set("status", "acknowledged")
                .Set("updated_at", now);

            var updateResult = await incidents.UpdateOneAsync(filter, update, cancellationToken: ct);

            if (updateResult.MatchedCount == 0)
            {
                return Results.BadRequest(new
                {
                    error = "concurrent_update",
                    message = "Incident was already acknowledged or modified by another admin.",
                });
            }

            return Results.Ok(new
            {
                incident_id = id,
                status = "acknowledged",
                message = "Incident acknowledged.",
            });
        }).RequireAuthorization();

        // PUT /api/v1/admin/incidents/{id}/resolve
        // Resolves an open or acknowledged incident. Step-up required.
        // For concurrency_conflict_unresolved incidents, also transitions the
        // associated position from Suspended back to Open or PendingEntry
        // (frozen-with-visibility resolution, REQ-RME-CONC-007 row 22).
        admin.MapPut("/incidents/{id}/resolve", async (
            string id,
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            var userId = GetUserId(context);
            if (string.IsNullOrEmpty(userId))
                return Results.Forbid();

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

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new { error = "invalid_incident_id" });

            var incidents = database.GetCollection<BsonDocument>("rme_incidents");
            var ct = context.RequestAborted;

            var incident = await incidents
                .Find(Builders<BsonDocument>.Filter.Eq("_id", objectId))
                .FirstOrDefaultAsync(ct);

            if (incident is null)
                return Results.NotFound(new { error = "incident_not_found" });

            var currentStatus = incident.GetValue("status", "").AsString;
            if (currentStatus is "resolved")
            {
                return Results.BadRequest(new
                {
                    error = "already_resolved",
                    message = "Incident is already resolved.",
                    current_status = currentStatus,
                });
            }

            var incidentType = incident.GetValue("incident_type", BsonNull.Value)?.AsString;
            var positionId = incident.GetValue("position_id", BsonNull.Value)?.AsString;
            var now = DateTime.UtcNow;

            // ── Frozen-with-visibility resolution for RME_OCC incidents ──────
            // REQ-RME-CONC-007: admin clears RME_OCC incident → position
            // transitions from Suspended to Open or PendingEntry.
            string? restoredState = null;

            if (incidentType == "concurrency_conflict_unresolved"
                && !string.IsNullOrEmpty(positionId)
                && Guid.TryParse(positionId, out var posGuid))
            {
                // Read the current position to determine restoration state.
                var positions = database.GetCollection<BsonDocument>("positions");
                var position = await positions
                    .Find(Builders<BsonDocument>.Filter.Eq("_id", posGuid))
                    .FirstOrDefaultAsync(ct);

                if (position is not null)
                {
                    var currentState = position.GetValue("state", "").AsString;
                    if (currentState == "Suspended")
                    {
                        // Determine restoration state based on entry price.
                        var entryPrice = position.GetValue("entry_price", BsonNull.Value);
                        var restoreTo = (entryPrice is BsonNull || entryPrice.IsBsonNull
                                        || (entryPrice.IsDouble && entryPrice.AsDouble == 0)
                                        || (entryPrice.IsDecimal128 && entryPrice.AsDecimal128 == 0))
                            ? "PendingEntry"
                            : "Open";

                        restoredState = restoreTo;

                        // Write audit event for the position transition.
                        await auditRepo.RecordStepUpGatedAsync(
                            userId,
                            "rme_occ_incident_resolved_with_transition",
                            now,
                            session!.StepUpEventId!.Value,
                            details: new Dictionary<string, object?>
                            {
                                ["incident_id"] = id,
                                ["position_id"] = positionId,
                                ["previous_state"] = currentState,
                                ["restored_state"] = restoreTo,
                                ["incident_type"] = incidentType,
                            },
                            cancellationToken: ct);

                        // Update position: clear suspension and restore state.
                        var positionFilter = Builders<BsonDocument>.Filter.Eq("_id", posGuid);
                        var positionUpdate = Builders<BsonDocument>.Update
                            .Set("state", restoreTo)
                            .Unset("suspension_reason")
                            .Unset("suspension_source")
                            .Set("updated_at", now.ToString("o"));

                        await positions.UpdateOneAsync(positionFilter, positionUpdate, cancellationToken: ct);
                    }
                }
            }

            // ── Resolve the incident ─────────────────────────────────────────
            var resolveFilter = Builders<BsonDocument>.Filter.Eq("_id", objectId);
            var resolveUpdate = Builders<BsonDocument>.Update
                .Set("status", "resolved")
                .Set("resolved_at", now)
                .Set("resolved_by", userId)
                .Set("updated_at", now);

            await incidents.UpdateOneAsync(resolveFilter, resolveUpdate, cancellationToken: ct);

            // Write audit event for the incident resolution.
            await auditRepo.RecordStepUpGatedAsync(
                userId,
                "incident_resolved",
                now,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["incident_id"] = id,
                    ["incident_type"] = incidentType,
                    ["position_id"] = positionId,
                    ["resolved_with_transition"] = restoredState is not null,
                    ["restored_state"] = restoredState,
                },
                cancellationToken: ct);

            return Results.Ok(new
            {
                incident_id = id,
                status = "resolved",
                incident_type = incidentType,
                position_id = positionId,
                position_transitioned = restoredState is not null,
                restored_state = restoredState,
                message = restoredState is not null
                    ? $"Incident resolved. Position restored to {restoredState}."
                    : "Incident resolved.",
            });
        }).RequireAuthorization();

        return app;
    }

    /// <summary>
    /// Validates the caller is an admin with active step-up re-authentication.
    /// Returns an error IResult if validation fails, or null on success.
    /// </summary>
    private static async Task<IResult?> VerifyAdminAndStepUp(
        HttpContext context,
        ISessionRepository sessionRepo,
        IUserRepository userRepo)
    {
        var userId = GetUserId(context);
        if (string.IsNullOrEmpty(userId))
            return Results.Forbid();

        var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
        if (user is null || user.Role != UserRole.Admin)
            return Results.Forbid();

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

    private static string GetUserId(HttpContext context) =>
        context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

    /// <summary>
    /// Maps a raw BSON incident document to a response-safe anonymous shape.
    /// </summary>
    private static object MapIncident(BsonDocument doc)
    {
        var id = doc.GetValue("_id", BsonNull.Value);
        return new
        {
            id = id.IsBsonNull ? null : id.AsObjectId.ToString(),
            position_id = doc.GetValue("position_id", BsonNull.Value)?.AsString,
            user_id = doc.GetValue("user_id", BsonNull.Value)?.AsString,
            incident_type = doc.GetValue("incident_type", BsonNull.Value)?.AsString,
            severity = doc.GetValue("severity", BsonNull.Value)?.AsString,
            status = doc.GetValue("status", BsonNull.Value)?.AsString ?? "unknown",
            detail = doc.GetValue("detail", BsonNull.Value)?.AsBsonDocument?.ToDictionary(),
            created_at = doc.GetValue("created_at", BsonNull.Value)?.ToNullableUniversalTime(),
            updated_at = doc.GetValue("updated_at", BsonNull.Value)?.ToNullableUniversalTime(),
            resolved_at = doc.GetValue("resolved_at", BsonNull.Value)?.ToNullableUniversalTime(),
            resolved_by = doc.GetValue("resolved_by", BsonNull.Value)?.AsString,
        };
    }
}
