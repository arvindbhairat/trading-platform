using System.Text.Json;
using System.Security.Claims;
using MongoDB.Bson;
using SignalStack.Api.Auth;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using SignalStack.Storage.SysConfig;

namespace SignalStack.Api.Admin;

public static class AdminConfigEndpoints
{
    // REQ-SEC-011: these categories require step-up re-authentication to edit.
    private static readonly HashSet<string> SensitiveCategories =
        ["risk", "legal", "operations"];

    // Matches the 5-minute window in AuthEndpoints (REQ-SEC-011).
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapAdminConfigEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/config — list all config entries, optional ?category= filter
        // REQ-CONFIG-005/007: category-based listing.
        admin.MapGet("/config", async (
            HttpContext context,
            ISysConfigRepository configRepo,
            string? category) =>
        {
            var entries = await configRepo.ListAllAsync(category, context.RequestAborted);
            var result = entries.Select(doc => MapToEntry(doc));
            return Results.Ok(new ConfigEntriesResponse(Entries: result));
        }).RequireAuthorization();

        // GET /api/v1/admin/config/categories — list distinct categories
        admin.MapGet("/config/categories", async (
            HttpContext context,
            ISysConfigRepository configRepo) =>
        {
            var categories = await configRepo.ListCategoriesAsync(context.RequestAborted);
            return Results.Ok(new ConfigCategoriesResponse(Categories: categories));
        }).RequireAuthorization();

        // GET /api/v1/admin/config/{key} — get single entry
        admin.MapGet("/config/{key}", async (
            string key,
            HttpContext context,
            ISysConfigRepository configRepo) =>
        {
            var doc = await configRepo.GetByKeyAsync(key, context.RequestAborted);
            if (doc is null)
                return Results.NotFound(new AdminSimpleErrorResponse(Error: "config_key_not_found"));

            return Results.Ok(new ConfigEntryResponse(Entry: MapToEntry(doc)));
        }).RequireAuthorization();

        // PUT /api/v1/admin/config/{key} — update a config value
        // REQ-CONFIG-005: audit trail on every edit.
        // REQ-SEC-011: step-up required for risk/legal/operations categories.
        // REQ-LEGAL-001: phase transitions require justification + step-up audit chain.
        // CSRF-protected via the global CsrfMiddleware.
        admin.MapPut("/config/{key}", async (
            string key,
            HttpContext context,
            ISysConfigRepository configRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            // Verify admin role.
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var body = await context.Request.ReadFromJsonAsync<UpdateConfigRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new AdminSimpleErrorResponse(Error: "invalid_body"));

            // Fetch current entry before mutation for audit and step-up check.
            var current = await configRepo.GetByKeyAsync(key, context.RequestAborted);
            if (current is null)
                return Results.NotFound(new AdminSimpleErrorResponse(Error: "config_key_not_found"));

            if (!current.Contains("isEditable") || !current["isEditable"].AsBoolean)
                return Results.BadRequest(new AdminSimpleErrorResponse(Error: "config_key_not_editable"));

            var category = current.Contains("category") ? current["category"].AsString : "";
            var valueType = current.Contains("valueType") ? current["valueType"].AsString : "string";
            var priorValue = ExtractValue(current, "value");

            // P8-T3 / REQ-LEGAL-001: phase transitions must use the dedicated
            // phase transition endpoint with full gating validation.
            if (key == "operations.phase.current")
            {
                return Results.BadRequest(new DedicatedPhaseEndpointResponse(
                    Error: "use_dedicated_phase_endpoint",
                    Message: "Phase transitions must go through the dedicated " +
                              "POST /api/v1/admin/phase/transition endpoint which " +
                              "validates all gating conditions (REQ-LEGAL-005)."));
            }

            // REQ-SEC-011: step-up check for sensitive categories.
            // REQ-LEGAL-001: justification required for sensitive category edits.
            ObjectId? stepUpEventId = null;
            if (SensitiveCategories.Contains(category))
            {
                if (string.IsNullOrWhiteSpace(body.Justification))
                {
                    return Results.BadRequest(new AdminSimpleErrorResponse(Error: "justification_required"));
                }

                var jti = context.User.FindFirst("jti")?.Value ?? "";
                var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
                var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                    && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
                if (!stepUpValid)
                {
                    return Results.Json(
                        new AdminStepUpRequiredResponse(Error: "step_up_required", Category: category),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                stepUpEventId = session!.StepUpEventId;
            }

            // Convert the incoming JSON value to BsonValue based on valueType.
            BsonValue newBsonValue;
            try
            {
                newBsonValue = ConvertJsonElementToBsonValue(body.Value, valueType);
            }
            catch (FormatException)
            {
                return Results.BadRequest(new InvalidValueResponse(Error: "invalid_value_for_type", ValueType: valueType));
            }

            // Build shared audit details (REQ-CONFIG-005/005a, REQ-LEGAL-001).
            var auditDetails = new Dictionary<string, object?>
            {
                ["key"] = key,
                ["category"] = category,
                ["value_type"] = valueType,
                ["prior_value"] = priorValue,
                ["new_value"] = body.Value,
            };
            if (!string.IsNullOrWhiteSpace(body.Justification))
                auditDetails["justification"] = body.Justification;

            // Write audit event BEFORE mutation.
            if (stepUpEventId.HasValue)
            {
                await auditRepo.RecordStepUpGatedAsync(
                    userId,
                    "config_updated",
                    DateTime.UtcNow,
                    stepUpEventId.Value,
                    details: auditDetails,
                    cancellationToken: context.RequestAborted);
            }
            else
            {
                await auditRepo.RecordAsync(
                    userId,
                    "config_updated",
                    DateTime.UtcNow,
                    details: auditDetails,
                    cancellationToken: context.RequestAborted);
            }

            // Perform the mutation.
            var updated = await configRepo.UpdateAsync(
                key, newBsonValue, userId, context.RequestAborted);

            if (updated is null)
                return Results.NotFound(new AdminSimpleErrorResponse(Error: "config_key_not_found"));

            return Results.Ok(new ConfigEntryResponse(Entry: MapToEntry(updated)));
        }).RequireAuthorization();

        // POST /api/v1/admin/config/{key}/reset — reset a config value to its default
        // REQ-CONFIG-005a: reset must produce an audit event.
        // REQ-SEC-011: step-up required for risk/legal/operations categories.
        // REQ-LEGAL-001: justification required for sensitive category resets.
        admin.MapPost("/config/{key}/reset", async (
            string key,
            HttpContext context,
            ISysConfigRepository configRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo,
            IUserRepository userRepo) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            // Verify admin role.
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var body = await context.Request.ReadFromJsonAsync<ResetConfigRequest>(
                cancellationToken: context.RequestAborted);

            // Fetch current entry before mutation for audit and step-up check.
            var current = await configRepo.GetByKeyAsync(key, context.RequestAborted);
            if (current is null)
                return Results.NotFound(new AdminSimpleErrorResponse(Error: "config_key_not_found"));

            var category = current.Contains("category") ? current["category"].AsString : "";
            var priorValue = ExtractValue(current, "value");
            var defaultValue = ExtractValue(current, "defaultValue");

            // REQ-SEC-011: step-up check for sensitive categories.
            // REQ-LEGAL-001: justification required for sensitive category resets.
            ObjectId? stepUpEventId = null;
            if (SensitiveCategories.Contains(category))
            {
                if (body is null || string.IsNullOrWhiteSpace(body.Justification))
                {
                    return Results.BadRequest(new AdminSimpleErrorResponse(Error: "justification_required"));
                }

                var jti = context.User.FindFirst("jti")?.Value ?? "";
                var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
                var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                    && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
                if (!stepUpValid)
                {
                    return Results.Json(
                        new AdminStepUpRequiredResponse(Error: "step_up_required", Category: category),
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                stepUpEventId = session!.StepUpEventId;
            }

            // Build shared audit details (REQ-CONFIG-005a, REQ-LEGAL-001).
            var auditDetails = new Dictionary<string, object?>
            {
                ["key"] = key,
                ["category"] = category,
                ["prior_value"] = priorValue,
                ["default_value"] = defaultValue,
            };
            if (body?.Justification is not null)
                auditDetails["justification"] = body.Justification;

            // Write audit event BEFORE mutation (REQ-CONFIG-005a).
            if (stepUpEventId.HasValue)
            {
                await auditRepo.RecordStepUpGatedAsync(
                    userId,
                    "config_reset",
                    DateTime.UtcNow,
                    stepUpEventId.Value,
                    details: auditDetails,
                    cancellationToken: context.RequestAborted);
            }
            else
            {
                await auditRepo.RecordAsync(
                    userId,
                    "config_reset",
                    DateTime.UtcNow,
                    details: auditDetails,
                    cancellationToken: context.RequestAborted);
            }

            // Perform the reset.
            var updated = await configRepo.ResetToDefaultAsync(
                key, userId, context.RequestAborted);

            if (updated is null)
                return Results.NotFound(new AdminSimpleErrorResponse(Error: "config_key_not_found"));

            return Results.Ok(new ConfigEntryResponse(Entry: MapToEntry(updated)));
        }).RequireAuthorization();

        return app;
    }

    private static SysConfigEntry MapToEntry(BsonDocument doc)
    {
        return new SysConfigEntry(
            Key: doc.GetValue("key", "").AsString,
            Category: doc.GetValue("category", "").AsString,
            ValueType: doc.GetValue("valueType", "string").AsString,
            Value: ExtractValue(doc, "value"),
            DefaultValue: ExtractValue(doc, "defaultValue"),
            Description: doc.GetValue("description", "").AsString,
            AppliesTo: doc.GetValue("appliesTo", BsonNull.Value).IsBsonNull
                ? []
                : doc["appliesTo"].AsBsonArray.Select(v => v.AsString).ToArray(),
            IsEditable: doc.GetValue("isEditable", BsonBoolean.True).AsBoolean,
            RequiresRestart: doc.GetValue("requiresRestart", BsonBoolean.False).AsBoolean,
            Status: doc.GetValue("status", "active").AsString,
            UpdatedAt: doc.GetValue("updatedAt", BsonString.Empty) switch
            {
                BsonDateTime dt => dt.ToUniversalTime().ToString("o"),
                BsonString s => s.AsString,
                _ => ""
            },
            UpdatedByUserId: doc.GetValue("updatedByUserId", "").AsString,
            Version: doc.GetValue("version", 1) switch
            {
                BsonInt32 i32 => i32.AsInt32,
                BsonInt64 i64 => (int)i64.AsInt64,
                _ => 1
            }
        );
    }

    private static object? ExtractValue(BsonDocument doc, string field)
    {
        if (!doc.Contains(field)) return null;
        var val = doc[field];
        return val switch
        {
            null or BsonNull => null,
            BsonString s => s.AsString,
            BsonInt32 i => i.AsInt32,
            BsonInt64 l => l.AsInt64,
            BsonDouble d => d.AsDouble,
            BsonBoolean b => b.AsBoolean,
            _ => val.ToString()
        };
    }

    private static BsonValue ConvertJsonElementToBsonValue(JsonElement el, string valueType)
    {
        if (valueType == "number")
        {
            return el.ValueKind switch
            {
                JsonValueKind.Number => el.TryGetInt64(out var l)
                    ? BsonValue.Create(l)
                    : BsonValue.Create(el.GetDouble()),
                JsonValueKind.String => BsonValue.Create(long.Parse(el.GetString()!)),
                _ => throw new FormatException()
            };
        }

        if (valueType == "boolean")
        {
            return el.ValueKind switch
            {
                JsonValueKind.True => BsonBoolean.True,
                JsonValueKind.False => BsonBoolean.False,
                JsonValueKind.String => bool.TryParse(el.GetString(), out var b)
                    ? BsonValue.Create(b)
                    : throw new FormatException(),
                _ => throw new FormatException()
            };
        }

        // Default to string.
        return new BsonString(el.ValueKind == JsonValueKind.String
            ? el.GetString()!
            : el.GetRawText());
    }
}

public sealed record SysConfigEntry(
    string Key,
    string Category,
    string ValueType,
    object? Value,
    object? DefaultValue,
    string Description,
    string[] AppliesTo,
    bool IsEditable,
    bool RequiresRestart,
    string Status,
    string UpdatedAt,
    string UpdatedByUserId,
    int Version
);

public sealed record UpdateConfigRequest(JsonElement Value, string? Justification = null);

/// <summary>
/// Request body for resetting a config value to its default.
/// REQ-LEGAL-001: justification is required for sensitive category resets.
/// </summary>
public sealed record ResetConfigRequest(string? Justification = null);

public sealed record ConfigEntriesResponse(IEnumerable<SysConfigEntry> Entries);
public sealed record ConfigCategoriesResponse(IEnumerable<string> Categories);
public sealed record ConfigEntryResponse(SysConfigEntry Entry);
public sealed record InvalidValueResponse(string Error, string ValueType);
public sealed record DedicatedPhaseEndpointResponse(string Error, string Message);
