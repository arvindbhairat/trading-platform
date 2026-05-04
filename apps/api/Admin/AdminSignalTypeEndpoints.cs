using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin endpoints for Signal type enable/disable (REQ-ADMIN-011, REQ-ADMIN-013).
/// When a Signal type is disabled platform-wide, positions opened by subscriptions
/// of that type are suspended with reason signal_type_disabled.
/// All mutations require step-up re-authentication (REQ-SEC-011).
/// </summary>
public static class AdminSignalTypeEndpoints
{
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    // Known Signal type IDs. In a future phase these will be discoverable
    // from the evaluator registry; for now they are enumerated here.
    // REQ-STRAT-001a: stable identifiers.
    private static readonly HashSet<string> KnownSignalTypes = new(StringComparer.Ordinal)
    {
        "ma_crossover",
    };

    // Human-readable display names for known Signal types.
    private static readonly Dictionary<string, string> SignalTypeNames = new(StringComparer.Ordinal)
    {
        ["ma_crossover"] = "MA Crossover",
    };

    public static IEndpointRouteBuilder MapAdminSignalTypeEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/signal-types
        // Lists all known Signal types with their enabled/disabled status.
        // Reads disabled state from sys_config risk.signal_types.{id}.enabled.
        // REQ-ADMIN-011/013: visibility for admin portal.
        admin.MapGet("/signal-types", async (
            IMongoDatabase database) =>
        {
            var sysConfig = database.GetCollection<BsonDocument>("sys_config");

            // Read all risk.signal_types.*.enabled entries in one query.
            var cursor = await sysConfig.FindAsync(
                Builders<BsonDocument>.Filter.Regex("key", "^risk\\.signal_types\\."));

            var configDocs = await cursor.ToListAsync();
            var enabledMap = new Dictionary<string, bool>(StringComparer.Ordinal);

            foreach (var doc in configDocs)
            {
                var key = doc.GetValue("key", "").AsString;
                // Extract signal type ID from key: "risk.signal_types.{id}.enabled"
                var parts = key.Split('.');
                if (parts.Length >= 3 && doc.TryGetValue("value", out var val) && val.IsBoolean)
                {
                    enabledMap[parts[2]] = val.AsBoolean;
                }
            }

            var result = KnownSignalTypes.Select(id => new
            {
                signal_type_id = id,
                name = SignalTypeNames.GetValueOrDefault(id, id),
                enabled = enabledMap.TryGetValue(id, out var e) ? e : true,
            });

            return Results.Ok(new { signal_types = result });
        }).RequireAuthorization();

        // POST /api/v1/admin/signal-types/{signalTypeId}/disable
        // Disables a Signal type platform-wide (step-up gated).
        // Open positions with this signal type will be suspended with
        // reason signal_type_disabled on the next RME cycle.
        // REQ-ADMIN-011: requires step-up.
        admin.MapPost("/signal-types/{signalTypeId}/disable", async (
            string signalTypeId,
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            return await ToggleSignalTypeAsync(
                context, sessionRepo, auditRepo, userRepo, database,
                signalTypeId, enable: false);
        }).RequireAuthorization();

        // POST /api/v1/admin/signal-types/{signalTypeId}/enable
        // Re-enables a previously disabled Signal type (step-up gated).
        // Suspended positions are not auto-released — admin must release
        // each affected position via REQ-ADMIN-016(d).
        // REQ-ADMIN-013: requires step-up.
        admin.MapPost("/signal-types/{signalTypeId}/enable", async (
            string signalTypeId,
            HttpContext context,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            return await ToggleSignalTypeAsync(
                context, sessionRepo, auditRepo, userRepo, database,
                signalTypeId, enable: true);
        }).RequireAuthorization();

        return app;
    }

    private static async Task<IResult> ToggleSignalTypeAsync(
        HttpContext context,
        ISessionRepository sessionRepo,
        IAuditEventRepository auditRepo,
        IUserRepository userRepo,
        IMongoDatabase database,
        string signalTypeId,
        bool enable)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

        // Verify admin role.
        var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
        if (user is null || user.Role != UserRole.Admin)
            return Results.Forbid();

        // Validate signal type ID.
        if (!KnownSignalTypes.Contains(signalTypeId))
        {
            return Results.BadRequest(new
            {
                error = "unknown_signal_type",
                message = $"Signal type '{signalTypeId}' is not recognised.",
                known_types = KnownSignalTypes
            });
        }

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

        var now = DateTime.UtcNow;
        var action = enable ? "enable" : "disable";
        var actionType = enable
            ? "signal_type_enabled"
            : "signal_type_disabled";

        // Write audit event.
        await auditRepo.RecordStepUpGatedAsync(
            userId,
            actionType,
            now,
            session!.StepUpEventId!.Value,
            details: new Dictionary<string, object?>
            {
                ["signal_type_id"] = signalTypeId,
                ["action"] = action,
            },
            cancellationToken: context.RequestAborted);

        // Update sys_config: risk.signal_types.{id}.enabled
        var sysConfig = database.GetCollection<BsonDocument>("sys_config");
        var configKey = $"risk.signal_types.{signalTypeId}.enabled";
        var filter = Builders<BsonDocument>.Filter.Eq("key", configKey);

        var update = Builders<BsonDocument>.Update
            .Set("value", BsonBoolean.Create(enable))
            .Set("updatedAt", now.ToString("o"))
            .Set("updatedByUserId", userId)
            .Inc("version", 1);

        var result = await sysConfig.UpdateOneAsync(filter, update, cancellationToken: context.RequestAborted);

        if (result.MatchedCount == 0)
        {
            // Key doesn't exist yet — insert it (for signal types not pre-seeded).
            var newDoc = new BsonDocument
            {
                ["key"] = configKey,
                ["value"] = BsonBoolean.Create(enable),
                ["category"] = "risk",
                ["valueType"] = "boolean",
                ["isEditable"] = true,
                ["requiresRestart"] = false,
                ["status"] = "active",
                ["version"] = 1,
                ["updatedAt"] = now.ToString("o"),
                ["updatedByUserId"] = userId,
            };
            await sysConfig.InsertOneAsync(newDoc, cancellationToken: context.RequestAborted);
        }

        var displayName = SignalTypeNames.GetValueOrDefault(signalTypeId, signalTypeId);

        return Results.Ok(new
        {
            signal_type_id = signalTypeId,
            name = displayName,
            enabled = enable,
            message = enable
                ? $"Signal type '{displayName}' re-enabled. Suspended positions remain suspended until admin release."
                : $"Signal type '{displayName}' disabled. Positions will be suspended on next RME cycle."
        });
    }
}
