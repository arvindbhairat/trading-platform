using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Universe;

public static class UniverseSyncEndpoints
{
    private const string Tag = "Universe";

    public static IEndpointRouteBuilder MapUniverseSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/universe");

        // POST /api/v1/admin/universe/upload/preview — upload CSV and get diff preview.
        // REQ-UNIV-017: pre-commit diff preview.
        admin.MapPost("/upload/preview", async (
            HttpContext context,
            UniverseSyncService syncService,
            IAuditEventRepository auditRepo,
            CancellationToken ct) =>
        {
            var adminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(adminId))
                return Results.Unauthorized();

            IFormFile? file;
            string csvContent;

            if (context.Request.HasFormContentType)
            {
                var form = await context.Request.ReadFormAsync(ct);
                file = form.Files.GetFile("file");
                if (file is null || file.Length == 0)
                    return Results.BadRequest(new { error = "CSV file is required." });

                using var reader = new StreamReader(file.OpenReadStream());
                csvContent = await reader.ReadToEndAsync(ct);
            }
            else
            {
                // Fallback: read raw text body.
                using var reader = new StreamReader(context.Request.Body);
                csvContent = await reader.ReadToEndAsync(ct);
                if (string.IsNullOrWhiteSpace(csvContent))
                    return Results.BadRequest(new { error = "CSV content is required." });
            }

            try
            {
                var preview = await syncService.GeneratePreviewAsync(csvContent, ct);

                // Determine if archive-confirm threshold is triggered. REQ-UNIV-018.
                // We need the active count to compute threshold here for the client.
                // The client will handle the threshold check before commit.
                return Results.Ok(new
                {
                    file_hash = preview.FileHash,
                    total_rows = preview.TotalRows,
                    accepted_rows = preview.AcceptedRows,
                    adds = preview.Adds.Select(a => new
                    {
                        symbol = a.Symbol,
                        company_name = a.CompanyName,
                        industry = a.Industry,
                        isin = a.Isin,
                        sql_table_name_suffix = a.SqlTableNameSuffix
                    }),
                    archives = preview.Archives.Select(a => new
                    {
                        symbol = a.Symbol,
                        company_name = a.CompanyName,
                        isin = a.Isin
                    }),
                    industry_changes = preview.IndustryChanges.Select(c => new
                    {
                        symbol = c.Symbol,
                        previous_industry = c.PreviousIndustry,
                        new_industry = c.NewIndustry
                    }),
                    skipped = preview.Skipped.Select(s => new
                    {
                        symbol = s.Symbol,
                        company_name = s.CompanyName,
                        series = s.Series
                    }),
                    rename_candidates = preview.RenameCandidates.Select(r => new
                    {
                        old_symbol = r.OldSymbol,
                        new_symbol = r.NewSymbol,
                        isin = r.Isin,
                        company_name = r.CompanyName,
                        industry = r.Industry
                    })
                });
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // POST /api/v1/admin/universe/upload/commit — commit an upload after admin confirms.
        // REQ-UNIV-011..020.
        admin.MapPost("/upload/commit", async (
            HttpContext context,
            CommitUploadRequest request,
            UniverseSyncService syncService,
            IAuditEventRepository auditRepo,
            CancellationToken ct) =>
        {
            var adminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(adminId))
                return Results.Unauthorized();

            var commitRequest = new UniverseSyncService.CommitRequest
            {
                AdminUserId = adminId,
                FileHash = request.FileHash,
                TotalRows = request.TotalRows,
                Adds = request.Adds.Select(a => new UniverseSyncService.DiffAddEntry
                {
                    Symbol = a.Symbol,
                    CompanyName = a.CompanyName,
                    Industry = a.Industry,
                    Isin = a.Isin,
                    SqlTableNameSuffix = a.SqlTableNameSuffix
                }).ToList(),
                Archives = request.Archives.Select(a => new UniverseSyncService.DiffArchiveEntry
                {
                    Symbol = a.Symbol,
                    CompanyName = a.CompanyName,
                    Isin = a.Isin
                }).ToList(),
                IndustryChanges = request.IndustryChanges.Select(c => new UniverseSyncService.DiffIndustryChange
                {
                    Symbol = c.Symbol,
                    PreviousIndustry = c.PreviousIndustry,
                    NewIndustry = c.NewIndustry
                }).ToList(),
                Skipped = request.Skipped.Select(s => new UniverseSyncService.DiffSkippedEntry
                {
                    Symbol = s.Symbol,
                    CompanyName = s.CompanyName,
                    Series = s.Series
                }).ToList(),
                RenameResolutions = request.RenameResolutions.Select(r => new UniverseSyncService.RenameResolution
                {
                    OldSymbol = r.OldSymbol,
                    NewSymbol = r.NewSymbol,
                    Isin = r.Isin,
                    Resolution = r.Resolution,
                    CompanyName = r.CompanyName ?? "",
                    Industry = r.Industry ?? ""
                }).ToList(),
                ConfirmationPhrase = request.ConfirmationPhrase,
                Justification = request.Justification
            };

            var result = await syncService.CommitAsync(commitRequest, ct);

            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            // Record audit event.
            var auditDetails = new Dictionary<string, object?>
            {
                ["action"] = "universe_sync_commit",
                ["upload_id"] = result.UploadId!.ToString(),
                ["new_symbols"] = string.Join(",", result.NewSymbols),
                ["archived_symbols"] = string.Join(",", result.ArchivedSymbols),
                ["file_hash"] = request.FileHash,
                ["rename_resolutions"] = string.Join(",",
                    request.RenameResolutions.Select(r => $"{r.OldSymbol}->{r.NewSymbol}:{r.Resolution}"))
            };

            if (!string.IsNullOrWhiteSpace(request.Justification))
                auditDetails["justification"] = request.Justification;

            if (!string.IsNullOrWhiteSpace(request.ConfirmationPhrase))
                auditDetails["confirmation_phrase"] = request.ConfirmationPhrase;

            await auditRepo.RecordAsync(adminId, "universe_sync_commit", DateTime.UtcNow, auditDetails, ct);

            return Results.Ok(new
            {
                upload_id = result.UploadId!.ToString(),
                new_symbols = result.NewSymbols,
                archived_symbols = result.ArchivedSymbols,
                hds_triggered = result.NewSymbols.Count > 0
            });
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // GET /api/v1/admin/universe/uploads — list upload history.
        // REQ-UNIV-011..020: audit trail.
        admin.MapGet("/uploads", async (
            IUniverseUploadRepository uploadRepo,
            CancellationToken ct) =>
        {
            var uploads = await uploadRepo.GetAllAsync(ct);
            return Results.Ok(new
            {
                uploads = uploads.Select(u => new
                {
                    id = u.Id.ToString(),
                    uploaded_by = u.UploadedBy,
                    uploaded_at = u.UploadedAt,
                    file_hash = u.FileHash,
                    total_rows = u.TotalRows,
                    rows_accepted = u.RowsAccepted,
                    rows_archived = u.RowsArchived,
                    rows_excluded = u.RowsExcluded,
                    new_symbols = u.NewSymbols,
                    archived_symbols = u.ArchivedSymbols,
                    hds_triggered = u.HdsTriggered,
                    rolled_back = u.RolledBack,
                    rolled_back_at = u.RolledBackAt,
                    rolled_back_by = u.RolledBackBy
                })
            });
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // POST /api/v1/admin/universe/upload/{id}/rollback — rollback an upload.
        // REQ-UNIV-019: 30-day sequential rollback.
        admin.MapPost("/upload/{id}/rollback", async (
            string id,
            HttpContext context,
            UniverseSyncService syncService,
            ISysConfigRepository configRepo,
            IAuditEventRepository auditRepo,
            CancellationToken ct) =>
        {
            var adminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(adminId))
                return Results.Unauthorized();

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new { error = "Invalid upload ID." });

            // Read retention period from sys_config (default 30).
            var retentionDays = 30;
            var configDoc = await configRepo.GetByKeyAsync("operations.universe.rollback_retention_days", ct);
            if (configDoc?.GetValue("value", 30) is BsonValue bv && bv.IsInt32)
                retentionDays = bv.AsInt32;

            var result = await syncService.RollbackAsync(objectId, adminId, retentionDays, ct);

            if (!result.Success)
                return Results.BadRequest(new { error = result.Error });

            // Record audit event.
            await auditRepo.RecordAsync(adminId, "universe_sync_rollback", DateTime.UtcNow,
                new Dictionary<string, object?>
                {
                    ["action"] = "universe_sync_rollback",
                    ["upload_id"] = id,
                    ["retention_days"] = retentionDays
                }, ct);

            return Results.Ok(new { status = "rolled_back", upload_id = id });
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        return app;
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────

/// <summary>Request body for POST /api/v1/admin/universe/upload/commit.</summary>
public sealed record CommitUploadRequest
{
    public required string FileHash { get; init; }
    public required int TotalRows { get; init; }
    public required List<CommitAddEntry> Adds { get; init; } = [];
    public required List<CommitArchiveEntry> Archives { get; init; } = [];
    public required List<CommitIndustryChange> IndustryChanges { get; init; } = [];
    public required List<CommitSkippedEntry> Skipped { get; init; } = [];
    public required List<CommitRenameResolution> RenameResolutions { get; init; } = [];
    public string? ConfirmationPhrase { get; init; }
    public string? Justification { get; init; }
}

public sealed record CommitAddEntry
{
    public required string Symbol { get; init; }
    public required string CompanyName { get; init; }
    public required string Industry { get; init; }
    public required string Isin { get; init; }
    public required string SqlTableNameSuffix { get; init; }
}

public sealed record CommitArchiveEntry
{
    public required string Symbol { get; init; }
    public required string CompanyName { get; init; }
    public required string Isin { get; init; }
}

public sealed record CommitIndustryChange
{
    public required string Symbol { get; init; }
    public required string PreviousIndustry { get; init; }
    public required string NewIndustry { get; init; }
}

public sealed record CommitSkippedEntry
{
    public required string Symbol { get; init; }
    public required string CompanyName { get; init; }
    public required string Series { get; init; }
}

public sealed record CommitRenameResolution
{
    public required string OldSymbol { get; init; }
    public required string NewSymbol { get; init; }
    public required string Isin { get; init; }
    public required string Resolution { get; init; }
    public string? CompanyName { get; init; }
    public string? Industry { get; init; }
}
