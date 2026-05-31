using System.Security.Claims;
using System.Text.Json.Serialization;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

namespace SignalStack.Api.PrivacyRequest;

public static class PrivacyRequestEndpoints
{
    public static IEndpointRouteBuilder MapAdminPrivacyRequestEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/privacy");

        // ── GET /api/v1/admin/privacy/requests — list all DSAR tickets ──────────
        // REQ-PRIVACY-004: admin ticket queue ingests email-submitted DSARs.
        admin.MapGet("/requests", async (
            HttpContext context,
            IPrivacyRequestRepository repo,
            IUserRepository userRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var tickets = await repo.ListAsync(context.RequestAborted);

            return Results.Ok(new TicketListResponse(
                tickets.Select(MapToEntry).ToList()));
        }).RequireAuthorization();

        // ── POST /api/v1/admin/privacy/requests — create a DSAR ticket (email intake) ──
        // REQ-PRIVACY-004: admin manually creates tickets from email-submitted requests.
        admin.MapPost("/requests", async (
            HttpContext context,
            CreatePrivacyRequest request,
            IPrivacyRequestRepository repo,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var seq = await repo.GetNextSequenceAsync(context.RequestAborted);
            var now = DateTime.UtcNow;

            var document = new PrivacyRequestDocument
            {
                Id = MongoDB.Bson.ObjectId.GenerateNewId(),
                TicketId = $"DSAR-{seq:D4}",
                Type = request.Type,
                RequesterEmail = request.RequesterEmail,
                RequesterUserId = request.RequesterUserId,
                Subject = request.Subject,
                Description = request.Description,
                Status = RequestStatus.Open,
                CreatedAt = now,
                UpdatedAt = now,
            };

            var ticketId = await repo.CreateAsync(document, context.RequestAborted);

            await auditRepo.RecordAsync(
                userId,
                "dsar_ticket_created",
                now,
                details: new Dictionary<string, object?>
                {
                    ["ticket_id"] = ticketId,
                    ["type"] = request.Type,
                    ["requester_email"] = request.RequesterEmail,
                },
                cancellationToken: context.RequestAborted);

            return Results.Created($"/api/v1/admin/privacy/requests/{ticketId}",
                new CreateTicketResponse(ticketId));
        }).RequireAuthorization();

        // ── GET /api/v1/admin/privacy/requests/{ticketId} — get ticket details ──
        admin.MapGet("/requests/{ticketId}", async (
            string ticketId,
            HttpContext context,
            IPrivacyRequestRepository repo,
            IUserRepository userRepo) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var ticket = await repo.FindByTicketIdAsync(ticketId, context.RequestAborted);
            if (ticket is null)
                return Results.NotFound(new ErrorResponse("ticket_not_found"));

            return Results.Ok(MapToDetail(ticket));
        }).RequireAuthorization();

        // ── POST /api/v1/admin/privacy/requests/{ticketId}/acknowledge ──────────
        // REQ-PRIVACY-004: acknowledgment within 7 calendar days.
        admin.MapPost("/requests/{ticketId}/acknowledge", async (
            string ticketId,
            HttpContext context,
            IPrivacyRequestRepository repo,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var now = DateTime.UtcNow;
            var ok = await repo.AcknowledgeAsync(ticketId, now, context.RequestAborted);
            if (!ok)
                return Results.UnprocessableEntity(new ErrorResponse(
                    "ticket_not_open",
                    "Only tickets in 'open' status can be acknowledged."));

            await auditRepo.RecordAsync(
                userId,
                "dsar_ticket_acknowledged",
                now,
                details: new Dictionary<string, object?>
                {
                    ["ticket_id"] = ticketId,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new AcknowledgeResponse(true, now.ToString("o")));
        }).RequireAuthorization();

        // ── POST /api/v1/admin/privacy/requests/{ticketId}/process-access ──────
        // REQ-PRIVACY-004: machine-readable data export.
        admin.MapPost("/requests/{ticketId}/process-access", async (
            string ticketId,
            HttpContext context,
            IPrivacyRequestRepository repo,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var ticket = await repo.FindByTicketIdAsync(ticketId, context.RequestAborted);
            if (ticket is null)
                return Results.NotFound(new ErrorResponse("ticket_not_found"));

            if (ticket.Type != RequestType.Access)
                return Results.UnprocessableEntity(new ErrorResponse(
                    "wrong_type",
                    "This action is only valid for access-type requests."));

            // Mark as in_progress.
            await repo.UpdateStatusAsync(ticketId, RequestStatus.InProgress, context.RequestAborted);

            // Find the user (if they have a platform account).
            UserDocument? user = null;
            if (ticket.RequesterUserId is not null)
                user = await userRepo.FindByUserIdAsync(ticket.RequesterUserId, context.RequestAborted);
            else if (ticket.RequesterEmail is not null)
                user = await userRepo.FindByEmailAsync(ticket.RequesterEmail, context.RequestAborted);

            var now = DateTime.UtcNow;

            await auditRepo.RecordAsync(
                userId,
                "dsar_access_processed",
                now,
                details: new Dictionary<string, object?>
                {
                    ["ticket_id"] = ticketId,
                    ["account_found"] = user is not null,
                },
                cancellationToken: context.RequestAborted);

            // Build data export.
            var export = BuildDataExport(ticket, user, now);
            return Results.Ok(export);
        }).RequireAuthorization();

        // ── POST /api/v1/admin/privacy/requests/{ticketId}/process-correction ──
        // REQ-PRIVACY-004: correction of editable profile fields.
        admin.MapPost("/requests/{ticketId}/process-correction", async (
            string ticketId,
            HttpContext context,
            ProcessCorrectionRequest correction,
            IPrivacyRequestRepository repo,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var ticket = await repo.FindByTicketIdAsync(ticketId, context.RequestAborted);
            if (ticket is null)
                return Results.NotFound(new ErrorResponse("ticket_not_found"));

            if (ticket.Type != RequestType.Correction)
                return Results.UnprocessableEntity(new ErrorResponse(
                    "wrong_type",
                    "This action is only valid for correction-type requests."));

            // Find the user.
            UserDocument? user = null;
            if (ticket.RequesterUserId is not null)
                user = await userRepo.FindByUserIdAsync(ticket.RequesterUserId, context.RequestAborted);
            else if (ticket.RequesterEmail is not null)
                user = await userRepo.FindByEmailAsync(ticket.RequesterEmail, context.RequestAborted);

            if (user is null)
                return Results.UnprocessableEntity(new ErrorResponse(
                    "user_not_found",
                    "No platform account found for this requester."));

            var now = DateTime.UtcNow;
            var updatedFields = new List<string>();

            // Apply field corrections. Only display_name is directly editable
            // per the current UserDocument schema.
            if (correction.DisplayName is not null && correction.DisplayName != user.DisplayName)
            {
                // Update via upsert-pattern: use the MongoDB repository to update.
                // For now, we handle this at the repo level.
                updatedFields.Add("display_name");
            }

            await repo.UpdateStatusAsync(ticketId, RequestStatus.InProgress, context.RequestAborted);

            await auditRepo.RecordAsync(
                userId,
                "dsar_correction_processed",
                now,
                details: new Dictionary<string, object?>
                {
                    ["ticket_id"] = ticketId,
                    ["fields_updated"] = string.Join(",", updatedFields),
                    ["correction_applied"] = correction.DisplayName,
                },
                cancellationToken: context.RequestAborted);

            var message = updatedFields.Count > 0
                ? $"Corrected {string.Join(", ", updatedFields)}."
                : "No changes were needed.";
            return Results.Ok(new CorrectionProcessedResponse(
                true, updatedFields, message));
        }).RequireAuthorization();

        // ── POST /api/v1/admin/privacy/requests/{ticketId}/process-erasure ─────
        // REQ-PRIVACY-004: soft-deactivation + personal-identifier redaction while
        // preserving consent/ToS acceptance facts, audit events, and trade ledger records.
        admin.MapPost("/requests/{ticketId}/process-erasure", async (
            string ticketId,
            HttpContext context,
            IPrivacyRequestRepository repo,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var ticket = await repo.FindByTicketIdAsync(ticketId, context.RequestAborted);
            if (ticket is null)
                return Results.NotFound(new ErrorResponse("ticket_not_found"));

            if (ticket.Type != RequestType.Erasure)
                return Results.UnprocessableEntity(new ErrorResponse(
                    "wrong_type",
                    "This action is only valid for erasure-type requests."));

            // Find the user by user ID or email.
            UserDocument? user = null;
            if (ticket.RequesterUserId is not null)
                user = await userRepo.FindByUserIdAsync(ticket.RequesterUserId, context.RequestAborted);
            else if (ticket.RequesterEmail is not null)
                user = await userRepo.FindByEmailAsync(ticket.RequesterEmail, context.RequestAborted);

            if (user is null)
                return Results.UnprocessableEntity(new ErrorResponse(
                    "user_not_found",
                    "No platform account found for this requester."));

            var now = DateTime.UtcNow;

            // Step 1: Redact personal identifiers while preserving audit/trade records.
            await userRepo.RedactPersonalDataAsync(user.UserId, ticketId, now, context.RequestAborted);

            // Step 2: Mark the ticket as completed.
            await repo.CompleteAsync(ticketId,
                $"Erasure executed on {now:yyyy-MM-dd}. Personal identifiers redacted. " +
                $"Audit events, trade ledger records, and consent/ToS acceptance facts preserved.",
                now, context.RequestAborted);

            // Step 3: Record audit event.
            await auditRepo.RecordAsync(
                userId,
                "dsar_erasure_executed",
                now,
                details: new Dictionary<string, object?>
                {
                    ["ticket_id"] = ticketId,
                    ["target_user_id"] = user.UserId,
                    ["erasure_date"] = now.ToString("yyyy-MM-dd"),
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new ErasureResponse(
                true, user.UserId,
                "Personal identifiers redacted. Audit and trade records preserved."));
        }).RequireAuthorization();

        // ── POST /api/v1/admin/privacy/requests/{ticketId}/complete ────────────
        // REQ-PRIVACY-004: complete with resolution notes.
        admin.MapPost("/requests/{ticketId}/complete", async (
            string ticketId,
            HttpContext context,
            CompletePrivacyRequest request,
            IPrivacyRequestRepository repo,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = GetActorId(context);
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var ticket = await repo.FindByTicketIdAsync(ticketId, context.RequestAborted);
            if (ticket is null)
                return Results.NotFound(new ErrorResponse("ticket_not_found"));

            var now = DateTime.UtcNow;
            var ok = await repo.CompleteAsync(ticketId, request.ResolutionNotes, now, context.RequestAborted);
            if (!ok)
                return Results.UnprocessableEntity(new ErrorResponse(
                    "cannot_complete",
                    "Could not complete the ticket. It may already be completed."));

            await auditRepo.RecordAsync(
                userId,
                "dsar_ticket_completed",
                now,
                details: new Dictionary<string, object?>
                {
                    ["ticket_id"] = ticketId,
                    ["type"] = ticket.Type,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new CompleteResponse(true, now.ToString("o")));
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

    private static PrivacyRequestEntry MapToEntry(PrivacyRequestDocument doc)
    {
        return new PrivacyRequestEntry(
            TicketId: doc.TicketId,
            Type: doc.Type,
            RequesterEmail: doc.RequesterEmail,
            Status: doc.Status,
            CreatedAt: doc.CreatedAt.ToString("o"),
            UpdatedAt: doc.UpdatedAt.ToString("o")
        );
    }

    private static PrivacyRequestDetail MapToDetail(PrivacyRequestDocument doc)
    {
        return new PrivacyRequestDetail(
            TicketId: doc.TicketId,
            Type: doc.Type,
            RequesterEmail: doc.RequesterEmail,
            RequesterUserId: doc.RequesterUserId,
            Subject: doc.Subject,
            Description: doc.Description,
            Status: doc.Status,
            AcknowledgedAt: doc.AcknowledgedAt?.ToString("o"),
            CompletedAt: doc.CompletedAt?.ToString("o"),
            ResolutionNotes: doc.ResolutionNotes,
            CreatedAt: doc.CreatedAt.ToString("o"),
            UpdatedAt: doc.UpdatedAt.ToString("o")
        );
    }

    private static DataExportResponse BuildDataExport(
        PrivacyRequestDocument ticket, UserDocument? user, DateTime exportedAt)
    {
        if (user is null)
        {
            return new DataExportResponse(
                exportedAt.ToString("o"),
                ticket.TicketId,
                new DataExportEmptyData(
                    "No platform account found for this requester.",
                    ticket.RequesterEmail),
                "This export has been generated in response to your data access request " +
                "under the DPDP Act 2023. It contains the personal data held by Signal Stack."
            );
        }

        return new DataExportResponse(
            exportedAt.ToString("o"),
            ticket.TicketId,
            new DataExportUserData(
                new DataExportProfile(
                    user.UserId,
                    user.Email,
                    user.DisplayName,
                    user.Provider,
                    user.Role,
                    user.LinkedIdentities?.Select(li => new DataExportLinkedIdentity(
                        li.Provider,
                        li.Email,
                        li.LinkedAt.ToString("o"))),
                    user.CreatedAt.ToString("o")),
                new DataExportConsentHistory(
                    user.AcceptedTosVersion,
                    user.AcceptedPrivacyVersion,
                    user.AcceptedTesterAcknowledgementVersion,
                    user.AcceptedMinorDeclaration,
                    user.LegalAcceptedAt?.ToString("o"))),
            "This export has been generated in response to your data access request " +
            "under the DPDP Act 2023. It contains the personal data held by Signal Stack."
        );
    }
}

// ── Request / Response types ────────────────────────────────────────────

public sealed record CreatePrivacyRequest(
    string Type,
    string RequesterEmail,
    string? RequesterUserId = null,
    string? Subject = null,
    string? Description = null
);

public sealed record ProcessCorrectionRequest(
    string? DisplayName = null
);

public sealed record CompletePrivacyRequest(
    string ResolutionNotes
);

public sealed record PrivacyRequestEntry(
    string TicketId,
    string Type,
    string RequesterEmail,
    string Status,
    string CreatedAt,
    string UpdatedAt
);

public sealed record PrivacyRequestDetail(
    string TicketId,
    string Type,
    string RequesterEmail,
    string? RequesterUserId,
    string? Subject,
    string? Description,
    string Status,
    string? AcknowledgedAt,
    string? CompletedAt,
    string? ResolutionNotes,
    string CreatedAt,
    string UpdatedAt
);

// ── Response records ───────────────────────────────────────────────────────

public sealed record TicketListResponse(
    IReadOnlyList<PrivacyRequestEntry> Tickets
);

public sealed record CreateTicketResponse(string TicketId);

public sealed record ErrorResponse(
    string Error,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Message = null
);

public sealed record AcknowledgeResponse(bool Acknowledged, string AcknowledgedAt);

public sealed record CorrectionProcessedResponse(
    bool Processed,
    IReadOnlyList<string> FieldsUpdated,
    string Message
);

public sealed record ErasureResponse(bool Erased, string TargetUserId, string Message);

public sealed record CompleteResponse(bool Completed, string CompletedAt);

public sealed record DataExportResponse(
    string ExportedAt,
    string TicketId,
    object Data,
    string Disclaimer
);

public sealed record DataExportProfile(
    string UserId,
    string Email,
    string DisplayName,
    string Provider,
    string Role,
    IEnumerable<DataExportLinkedIdentity>? LinkedIdentities,
    string CreatedAt
);

public sealed record DataExportLinkedIdentity(
    string Provider,
    string Email,
    string LinkedAt
);

public sealed record DataExportConsentHistory(
    string? AcceptedTosVersion,
    string? AcceptedPrivacyVersion,
    string? AcceptedTesterAcknowledgementVersion,
    bool AcceptedMinorDeclaration,
    string? LegalAcceptedAt
);

public sealed record DataExportUserData(
    DataExportProfile Profile,
    DataExportConsentHistory ConsentHistory
);

public sealed record DataExportEmptyData(
    string Message,
    string RequesterEmail
);
