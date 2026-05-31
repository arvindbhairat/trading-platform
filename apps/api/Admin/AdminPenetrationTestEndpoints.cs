using System.Security.Claims;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Admin;
using SignalStack.Domain.Users;
using SignalStack.Storage.Admin;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin REST endpoints for penetration test scheduling, findings management,
/// and remediation tracking. P8-T10 — REQ-SEC-010.
/// </summary>
public static class AdminPenetrationTestEndpoints
{
    // REQ-SEC-011: penetration test management requires step-up (legal/security category).
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    private static readonly string[] AllowedScopes = ["portal", "api", "worker", "admin"];
    private static readonly string[] AllowedSeverities = ["critical", "high", "medium", "low", "info"];
    private static readonly string[] AllowedFindingStatuses = ["open", "in_progress", "remediated", "accepted", "false_positive"];
    private static readonly string[] AllowedTestStatuses = ["scheduled", "in_progress", "completed"];

    public static IEndpointRouteBuilder MapAdminPenetrationTestEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // ────────────────────────────────────────────────────────────────────
        // POST /api/v1/admin/pentest
        // Schedule a new external penetration test engagement.
        // REQ-SEC-010: record of scheduled test.
        // REQ-SEC-011: step-up required (legal category).
        admin.MapPost("/pentest", async (
            HttpContext context,
            IPenetrationTestRepository pentestRepo,
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

            var body = await context.Request.ReadFromJsonAsync<SchedulePentestRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new AdminErrorResponse("invalid_body"));

            if (string.IsNullOrWhiteSpace(body.VendorName))
                return Results.BadRequest(new AdminErrorResponse("vendor_name_required"));

            if (body.Scope is null || body.Scope.Count == 0)
                return Results.BadRequest(new AdminErrorResponse("scope_required"));

            var invalidScopes = body.Scope.Where(s => !AllowedScopes.Contains(s)).ToList();
            if (invalidScopes.Count > 0)
                return Results.BadRequest(new InvalidScopeValuesError("invalid_scope_values", invalidScopes));

            // REQ-SEC-011: step-up required (legal category).
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new AdminStepUpRequiredResponse("step_up_required", "legal"),
                    statusCode: StatusCodes.Status401Unauthorized);

            var doc = new PenetrationTestDocument
            {
                Id = ObjectId.GenerateNewId(),
                VendorName = body.VendorName,
                Scope = body.Scope,
                ScheduledDate = body.ScheduledDate ?? DateTime.UtcNow,
                Status = "scheduled",
                EngagementRef = body.EngagementRef,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await pentestRepo.CreateAsync(doc, context.RequestAborted);

            await auditRepo.RecordStepUpGatedAsync(
                userId, "pentest_scheduled", DateTime.UtcNow,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["pentest_id"] = doc.Id.ToString(),
                    ["vendor_name"] = doc.VendorName,
                    ["scope"] = string.Join(", ", doc.Scope),
                    ["scheduled_date"] = doc.ScheduledDate?.ToString("O"),
                    ["engagement_ref"] = doc.EngagementRef
                },
                cancellationToken: context.RequestAborted);

            return Results.Created($"/api/v1/admin/pentest/{doc.Id}",
                new PentestCreatedResponse(
                    doc.Id.ToString(),
                    doc.VendorName,
                    doc.Scope,
                    doc.Status,
                    doc.ScheduledDate?.ToString("O"),
                    doc.EngagementRef));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // POST /api/v1/admin/pentest/{id}/findings
        // Record a finding discovered during the penetration test.
        // REQ-SEC-010: findings tracking.
        // REQ-SEC-011: step-up required.
        admin.MapPost("/pentest/{id}/findings", async (
            string id,
            HttpContext context,
            IPenetrationTestRepository pentestRepo,
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

            var test = await pentestRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (test is null)
                return Results.NotFound(new AdminErrorResponse("pentest_not_found"));

            var body = await context.Request.ReadFromJsonAsync<AddFindingRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new AdminErrorResponse("invalid_body"));

            if (string.IsNullOrWhiteSpace(body.Title))
                return Results.BadRequest(new AdminErrorResponse("title_required"));
            if (string.IsNullOrWhiteSpace(body.Description))
                return Results.BadRequest(new AdminErrorResponse("description_required"));
            if (string.IsNullOrWhiteSpace(body.Severity) || !AllowedSeverities.Contains(body.Severity))
                return Results.BadRequest(new AdminAllowedValuesError("invalid_severity", AllowedSeverities));

            // REQ-SEC-011: step-up required (legal category).
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new AdminStepUpRequiredResponse("step_up_required", "legal"),
                    statusCode: StatusCodes.Status401Unauthorized);

            var findingId = $"PT-{DateTime.UtcNow:yyyyMMdd}-{(test.Findings?.Count ?? 0) + 1:D3}";

            var finding = new PenetrationTestFinding
            {
                FindingId = findingId,
                Title = body.Title,
                Description = body.Description,
                Severity = body.Severity,
                Status = "open",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await pentestRepo.AddFindingAsync(objectId, finding, context.RequestAborted);

            await auditRepo.RecordStepUpGatedAsync(
                userId, "pentest_finding_added", DateTime.UtcNow,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["pentest_id"] = id,
                    ["finding_id"] = findingId,
                    ["title"] = finding.Title,
                    ["severity"] = finding.Severity
                },
                cancellationToken: context.RequestAborted);

            return Results.Created($"/api/v1/admin/pentest/{id}/findings/{findingId}",
                new FindingCreatedResponse(findingId, body.Title, body.Severity, "open"));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // PATCH /api/v1/admin/pentest/{id}/findings/{findingId}
        // Update a finding's remediation status and notes.
        // REQ-SEC-010: remediation tracking.
        // REQ-SEC-011: step-up required.
        admin.MapPatch("/pentest/{id}/findings/{findingId}", async (
            string id,
            string findingId,
            HttpContext context,
            IPenetrationTestRepository pentestRepo,
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

            var test = await pentestRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (test is null)
                return Results.NotFound(new AdminErrorResponse("pentest_not_found"));

            var body = await context.Request.ReadFromJsonAsync<UpdateFindingRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new AdminErrorResponse("invalid_body"));

            if (body.Status is not null && !AllowedFindingStatuses.Contains(body.Status))
                return Results.BadRequest(new AdminAllowedValuesError("invalid_status", AllowedFindingStatuses));

            // REQ-SEC-011: step-up required (legal category).
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new AdminStepUpRequiredResponse("step_up_required", "legal"),
                    statusCode: StatusCodes.Status401Unauthorized);

            await pentestRepo.UpdateFindingAsync(
                objectId, findingId, body.Status, body.RemediationNotes, context.RequestAborted);

            await auditRepo.RecordStepUpGatedAsync(
                userId, "pentest_finding_updated", DateTime.UtcNow,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["pentest_id"] = id,
                    ["finding_id"] = findingId,
                    ["status"] = body.Status,
                    ["remediation_notes"] = body.RemediationNotes
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new FindingUpdatedResponse(true, findingId, body.Status));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // PATCH /api/v1/admin/pentest/{id}/status
        // Update the overall penetration test status.
        // REQ-SEC-011: step-up required.
        admin.MapPatch("/pentest/{id}/status", async (
            string id,
            HttpContext context,
            IPenetrationTestRepository pentestRepo,
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

            var test = await pentestRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (test is null)
                return Results.NotFound(new AdminErrorResponse("pentest_not_found"));

            var body = await context.Request.ReadFromJsonAsync<UpdatePentestStatusRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null)
                return Results.BadRequest(new AdminErrorResponse("invalid_body"));

            if (string.IsNullOrWhiteSpace(body.Status) || !AllowedTestStatuses.Contains(body.Status))
                return Results.BadRequest(new AdminAllowedValuesError("invalid_status", AllowedTestStatuses));

            // REQ-SEC-011: step-up required (legal category).
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new AdminStepUpRequiredResponse("step_up_required", "legal"),
                    statusCode: StatusCodes.Status401Unauthorized);

            var completedDate = body.Status == "completed" ? DateTime.UtcNow : test.CompletedDate;

            await pentestRepo.UpdateAsync(objectId,
                status: body.Status,
                completedDate: completedDate,
                ct: context.RequestAborted);

            await auditRepo.RecordStepUpGatedAsync(
                userId, "pentest_status_updated", DateTime.UtcNow,
                session!.StepUpEventId!.Value,
                details: new Dictionary<string, object?>
                {
                    ["pentest_id"] = id,
                    ["previous_status"] = test.Status,
                    ["new_status"] = body.Status,
                    ["completed_date"] = completedDate?.ToString("O")
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new PentestStatusUpdatedResponse(
                true, body.Status, completedDate?.ToString("O")));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // POST /api/v1/admin/pentest/{id}/mark-gate
        // Mark the penetration test Phase C gate as satisfied.
        // REQ-SEC-010: only permitted when no high/critical findings remain unresolved.
        // REQ-SEC-011: step-up required.
        admin.MapPost("/pentest/{id}/mark-gate", async (
            string id,
            HttpContext context,
            IPenetrationTestRepository pentestRepo,
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

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new AdminErrorResponse("invalid_id_format"));

            var test = await pentestRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (test is null)
                return Results.NotFound(new AdminErrorResponse("pentest_not_found"));

            // REQ-SEC-011: step-up required.
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;
            if (!stepUpValid)
                return Results.Json(
                    new AdminStepUpRequiredResponse("step_up_required", "legal"),
                    statusCode: StatusCodes.Status401Unauthorized);

            // REQ-SEC-010: verify all high/critical findings are remediated before marking gate.
            var unresolvedCount = await pentestRepo.CountUnresolvedHighCriticalFindingsAsync(
                objectId, context.RequestAborted);

            if (unresolvedCount > 0)
            {
                return Results.UnprocessableEntity(new UnresolvedFindingsError(
                    "unresolved_high_critical_findings",
                    $"Cannot mark penetration test gate as complete: {unresolvedCount} " +
                    "high/critical finding(s) are still unresolved. Remediate or accept " +
                    "all high and critical findings before marking the gate.",
                    unresolvedCount));
            }

            await gateSvc.SetGateAsync(
                userId, PhaseGateService.GatePenTest, true,
                $"Penetration test '{test.VendorName}' ({test.Id}) — all high/critical findings remediated.",
                context.RequestAborted);

            return Results.Ok(new PentestGateResponse(
                true, PhaseGateService.GatePenTest, true, id, test.VendorName));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // GET /api/v1/admin/pentest
        // List all penetration test engagements with findings summary.
        // REQ-SEC-010: visibility into test and remediation status.
        admin.MapGet("/pentest", async (
            HttpContext context,
            IPenetrationTestRepository pentestRepo,
            IUserRepository userRepo) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var tests = await pentestRepo.ListAllAsync(context.RequestAborted);

            var result = tests.Select(t =>
            {
                var f = t.Findings;
                var summary = new FindingsSummary(
                    f?.Count ?? 0,
                    f?.Count(ff => ff.Severity == "critical" && ff.Status is not ("remediated" or "false_positive")) ?? 0,
                    f?.Count(ff => ff.Severity == "high" && ff.Status is not ("remediated" or "false_positive")) ?? 0,
                    f?.Count(ff => ff.Severity == "medium" && ff.Status is not ("remediated" or "false_positive")) ?? 0,
                    f?.Count(ff => ff.Status == "remediated") ?? 0,
                    f?.Count(ff => ff.Status == "accepted") ?? 0);
                return new PentestListItem(
                    t.Id.ToString(),
                    t.VendorName,
                    t.Scope,
                    t.Status,
                    t.ScheduledDate?.ToString("O"),
                    t.CompletedDate?.ToString("O"),
                    t.EngagementRef,
                    summary,
                    t.CreatedAt.ToString("O"),
                    t.UpdatedAt.ToString("O"));
            }).ToList();

            return Results.Ok(new PentestListResponse(result));
        }).RequireAuthorization();

        // ────────────────────────────────────────────────────────────────────
        // GET /api/v1/admin/pentest/{id}
        // Get full penetration test engagement details including findings.
        admin.MapGet("/pentest/{id}", async (
            string id,
            HttpContext context,
            IPenetrationTestRepository pentestRepo,
            IUserRepository userRepo) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new AdminErrorResponse("invalid_id_format"));

            var test = await pentestRepo.GetByIdAsync(objectId, context.RequestAborted);
            if (test is null)
                return Results.NotFound(new AdminErrorResponse("pentest_not_found"));

            var findings = test.Findings?
                .OrderByDescending(f => f.Severity switch
                {
                    "critical" => 5,
                    "high" => 4,
                    "medium" => 3,
                    "low" => 2,
                    _ => 1
                })
                .ThenBy(f => f.FindingId)
                .Select(f => new PentestFindingResponse(
                    f.FindingId,
                    f.Title,
                    f.Description,
                    f.Severity,
                    f.Status,
                    f.RemediationNotes,
                    f.CreatedAt.ToString("O"),
                    f.UpdatedAt.ToString("O")
                )).ToList() ?? [];

            return Results.Ok(new PentestDetailResponse(
                test.Id.ToString(),
                test.VendorName,
                test.Scope,
                test.Status,
                test.ScheduledDate?.ToString("O"),
                test.CompletedDate?.ToString("O"),
                test.EngagementRef,
                findings,
                test.CreatedAt.ToString("O"),
                test.UpdatedAt.ToString("O")
            ));
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

public sealed record SchedulePentestRequest(
    string? VendorName,
    List<string>? Scope,
    DateTime? ScheduledDate,
    string? EngagementRef);

public sealed record AddFindingRequest(
    string? Title,
    string? Description,
    string? Severity);

public sealed record UpdateFindingRequest(
    string? Status,
    string? RemediationNotes);

public sealed record UpdatePentestStatusRequest(
    string? Status);

// ── Response DTOs ────────────────────────────────────────────────────────────

public sealed record PentestCreatedResponse(
    string Id, string? VendorName, List<string>? Scope,
    string Status, string? ScheduledDate, string? EngagementRef);

public sealed record FindingCreatedResponse(string FindingId, string? Title, string? Severity, string Status);

public sealed record FindingUpdatedResponse(bool Success, string FindingId, string? Status);

public sealed record PentestStatusUpdatedResponse(bool Success, string? Status, string? CompletedDate);

public sealed record PentestGateResponse(bool Success, string Gate, bool Passed, string PentestId, string? VendorName);

public sealed record FindingsSummary(int Total, int CriticalOpen, int HighOpen, int MediumOpen, int Remediated, int Accepted);

public sealed record PentestListItem(
    string Id, string? VendorName, List<string>? Scope, string Status,
    string? ScheduledDate, string? CompletedDate, string? EngagementRef,
    FindingsSummary FindingsSummary, string CreatedAt, string UpdatedAt);

public sealed record PentestListResponse(IEnumerable<PentestListItem> PenetrationTests);

public sealed record PentestFindingResponse(
    string FindingId, string? Title, string? Description, string? Severity,
    string? Status, string? RemediationNotes, string CreatedAt, string UpdatedAt);

public sealed record PentestDetailResponse(
    string Id, string? VendorName, List<string>? Scope, string Status,
    string? ScheduledDate, string? CompletedDate, string? EngagementRef,
    List<PentestFindingResponse> Findings, string CreatedAt, string UpdatedAt);

// ── Error DTOs ────────────────────────────────────────────────────────────

public sealed record InvalidScopeValuesError(string Error, List<string> InvalidValues);

public sealed record UnresolvedFindingsError(string Error, string Message, long UnresolvedHighCriticalCount);
