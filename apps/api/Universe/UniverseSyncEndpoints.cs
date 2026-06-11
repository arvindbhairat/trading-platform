using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Admin;
using SignalStack.Storage;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Notifications;
using SignalStack.Domain.Notifications;
using SignalStack.Storage.Admin;
using SignalStack.Storage.Notifications;
using SignalStack.Storage.SysConfig;
using SignalStack.Storage.Universe;

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
                    return Results.BadRequest(new ErrorResponse("CSV file is required."));

                using var reader = new StreamReader(file.OpenReadStream());
                csvContent = await reader.ReadToEndAsync(ct);
            }
            else
            {
                // Fallback: read raw text body.
                using var reader = new StreamReader(context.Request.Body);
                csvContent = await reader.ReadToEndAsync(ct);
                if (string.IsNullOrWhiteSpace(csvContent))
                    return Results.BadRequest(new ErrorResponse("CSV content is required."));
            }

            try
            {
                var preview = await syncService.GeneratePreviewAsync(csvContent, ct);

                // Determine if archive-confirm threshold is triggered. REQ-UNIV-018.
                // We need the active count to compute threshold here for the client.
                // The client will handle the threshold check before commit.
                return Results.Ok(new UploadPreviewResponse(
                    preview.FileHash,
                    preview.TotalRows,
                    preview.AcceptedRows,
                    preview.Adds.Select(a => new PreviewAddEntry(
                        a.Symbol,
                        a.CompanyName,
                        a.Industry,
                        a.Isin,
                        a.SqlTableNameSuffix
                    )).ToList(),
                    preview.Archives.Select(a => new PreviewArchiveEntry(
                        a.Symbol,
                        a.CompanyName,
                        a.Isin
                    )).ToList(),
                    preview.IndustryChanges.Select(c => new PreviewIndustryChange(
                        c.Symbol,
                        c.PreviousIndustry,
                        c.NewIndustry
                    )).ToList(),
                    preview.Skipped.Select(s => new PreviewSkippedEntry(
                        s.Symbol,
                        s.CompanyName,
                        s.Series
                    )).ToList(),
                    preview.RenameCandidates.Select(r => new PreviewRenameCandidate(
                        r.OldSymbol,
                        r.NewSymbol,
                        r.Isin,
                        r.CompanyName,
                        r.Industry
                    )).ToList()
                ));
            }
            catch (InvalidDataException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
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
                return Results.BadRequest(new ErrorResponse(result.Error!));

            // Auto-trigger HDS for net-new symbols (REQ-UNIV-014).
            string? hdsJobRunId = null;
            if (result.NewSymbols.Count > 0)
            {
                try
                {
                    var jobRuns = context.RequestServices
                        .GetRequiredService<IMongoDatabase>()
                        .GetCollection<BsonDocument>("job_runs");

                    var now = DateTime.UtcNow;
                    var newSuffixes = result.NewSymbols
                        .Select(s => SqlTableNameSuffixGenerator.Generate(s))
                        .ToList();

                    var hdsJobRun = new BsonDocument
                    {
                        ["job_type"] = "HDS",
                        ["trigger_source"] = "manual",
                        ["triggered_by"] = "universe_upload",
                        ["scheduled_run_time"] = now,
                        ["started_at"] = now,
                        ["outcome"] = BsonNull.Value,
                        ["symbols_processed"] = result.NewSymbols.Count,
                        ["symbols"] = BsonArray.Create(result.NewSymbols),
                        ["suffixes"] = BsonArray.Create(newSuffixes),
                        ["errors"] = new BsonArray(),
                        ["upload_id"] = result.UploadId!.ToString(),
                        ["created_by"] = adminId,
                        ["created_at"] = now
                    };

                    await jobRuns.InsertOneAsync(hdsJobRun, cancellationToken: ct);
                    hdsJobRunId = hdsJobRun["_id"].AsObjectId.ToString();
                }
                catch (Exception hdsEx)
                {
                    // Log but don't fail the commit — the upload result already records hds_triggered.
                    var logger = context.RequestServices
                        .GetRequiredService<ILogger<UniverseSyncService>>();                    logger.LogWarning(hdsEx,
                        "Failed to auto-trigger HDS job for new symbols in upload {UploadId}",
                        result.UploadId);
                }
            }

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

            if (hdsJobRunId is not null)
                auditDetails["hds_job_run_id"] = hdsJobRunId;

            if (!string.IsNullOrWhiteSpace(request.Justification))
                auditDetails["justification"] = request.Justification;

            if (!string.IsNullOrWhiteSpace(request.ConfirmationPhrase))
                auditDetails["confirmation_phrase"] = request.ConfirmationPhrase;

            await auditRepo.RecordAsync(adminId, "universe_sync_commit", DateTime.UtcNow, auditDetails, ct);

            // REQ-UNIV-015: notify users with open positions in archived symbols.
            if (result.ArchivedSymbols.Count > 0)
            {
                var notifDb = context.RequestServices.GetRequiredService<IMongoDatabase>();
                var notifWriter = context.RequestServices.GetRequiredService<INotificationWriter>();
                var notifLogger = context.RequestServices.GetRequiredService<ILogger<UniverseSyncService>>();
                await NotifyArchivedSymbolOpenPositionsAsync(
                    result.ArchivedSymbols, notifDb, notifWriter, notifLogger, ct);
            }

            return Results.Ok(new UploadCommitResponse(
                result.UploadId!.ToString()!,
                result.NewSymbols,
                result.ArchivedSymbols,
                result.NewSymbols.Count > 0,
                hdsJobRunId
            ));
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
            return Results.Ok(new UploadListResponse(
                uploads.Select(u => new UploadListItem(
                    u.Id.ToString(),
                    u.UploadedBy,
                    u.UploadedAt,
                    u.FileHash,
                    u.TotalRows,
                    u.RowsAccepted,
                    u.RowsArchived,
                    u.RowsExcluded,
                    u.NewSymbols,
                    u.ArchivedSymbols,
                    u.HdsTriggered,
                    u.RolledBack,
                    u.RolledBackAt,
                    u.RolledBackBy
                )).ToList()
            ));
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
                return Results.BadRequest(new ErrorResponse("Invalid upload ID."));

            // Read retention period from sys_config (default 30).
            var retentionDays = 30;
            var configDoc = await configRepo.GetByKeyAsync("operations.universe.rollback_retention_days", ct);
            if (configDoc?.GetValue("value", 30) is BsonValue bv && bv.IsInt32)
                retentionDays = bv.AsInt32;

            var result = await syncService.RollbackAsync(objectId, adminId, retentionDays, ct);

            if (!result.Success)
                return Results.BadRequest(new ErrorResponse(result.Error!));

            // Record audit event.
            await auditRepo.RecordAsync(adminId, "universe_sync_rollback", DateTime.UtcNow,
                new Dictionary<string, object?>
                {
                    ["action"] = "universe_sync_rollback",
                    ["upload_id"] = id,
                    ["retention_days"] = retentionDays
                }, ct);

            return Results.Ok(new UploadRollbackResponse("rolled_back", id));
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // GET /api/v1/admin/universe/sync-health — data sync health for all active symbols.
        // REQ-UNIV-015a: compares most recent candle date in SQL Server D_ table
        // against the most recent completed trading session.
        admin.MapGet("/sync-health", async (
            ISymbolMasterRepository symbolRepo,
            ISyncHealthRepository healthRepo,
            ITradingCalendarRepository calendarRepo,
            CancellationToken ct) =>
        {
            var symbols = await symbolRepo.GetAllAsync(archived: false, ct);
            if (symbols.Count == 0)
                return Results.Ok(new SyncHealthEmptyResponse(Array.Empty<object>(), 0));

            // Get the most recent completed trading session from the calendar.
            var sessions = await calendarRepo.GetSessionsAsync(
                fromDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)).ToString("yyyy-MM-dd"),
                ct: ct);

            // Sessions are ordered ascending; the last one is the most recent.
            var mostRecentSession = sessions.Count > 0
                ? DateOnly.Parse(sessions[^1].SessionDate)
                : (DateOnly?)null;

            // Get sync health from SQL Server for all active symbol suffixes.
            var suffixes = symbols.Select(s => s.SqlTableNameSuffix).Distinct().ToList();
            var healthResults = await healthRepo.GetSyncHealthAsync(suffixes, ct);
            var healthBySuffix = healthResults.ToDictionary(h => h.Suffix);

            var symbolHealthList = symbols.Select(s =>
            {
                var health = healthBySuffix.GetValueOrDefault(s.SqlTableNameSuffix);
                var lastCandleDate = health?.LastCandleDate;

                // A symbol is out of sync if:
                // - No candle data exists (lastCandleDate is null), OR
                // - The most recent candle date is before the most recent completed session
                bool outOfSync = true;
                if (lastCandleDate.HasValue && mostRecentSession.HasValue)
                {
                    outOfSync = DateOnly.FromDateTime(lastCandleDate.Value) < mostRecentSession.Value;
                }

                return new SymbolHealthItem(
                    s.Symbol,
                    s.CompanyName,
                    s.IsArchived,
                    s.ScanExcluded,
                    s.SqlTableNameSuffix,
                    lastCandleDate?.ToString("yyyy-MM-dd"),
                    mostRecentSession?.ToString("yyyy-MM-dd"),
                    outOfSync
                );
            }).ToList();

            var outOfSyncCount = symbolHealthList.Count(s => s.OutOfSync);

            return Results.Ok(new SyncHealthResponse(
                symbolHealthList,
                outOfSyncCount,
                symbols.Count,
                mostRecentSession?.ToString("yyyy-MM-dd")
            ));
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // POST /api/v1/admin/universe/reseed — trigger HDS reseed for all out-of-sync symbols.
        // REQ-UNIV-015b: one-click reseed invoking HistoricDataSeed scoped to out-of-sync symbols.
        admin.MapPost("/reseed", async (
            HttpContext context,
            ISymbolMasterRepository symbolRepo,
            ISyncHealthRepository healthRepo,
            ITradingCalendarRepository calendarRepo,
            IMongoDatabase database,
            ILogger<UniverseSyncService> logger,
            CancellationToken ct) =>
        {
            var adminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(adminId))
                return Results.Unauthorized();

            // Find out-of-sync symbols (same logic as sync-health endpoint).
            var symbols = await symbolRepo.GetAllAsync(archived: false, ct);
            if (symbols.Count == 0)
                return Results.Ok(new ReseedSimpleResponse("no_symbols", 0));

            var sessions = await calendarRepo.GetSessionsAsync(
                fromDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-90)).ToString("yyyy-MM-dd"),
                ct: ct);
            var mostRecentSession = sessions.Count > 0
                ? DateOnly.Parse(sessions[^1].SessionDate)
                : (DateOnly?)null;

            var suffixes = symbols.Select(s => s.SqlTableNameSuffix).Distinct().ToList();
            var healthResults = await healthRepo.GetSyncHealthAsync(suffixes, ct);
            var healthBySuffix = healthResults.ToDictionary(h => h.Suffix);

            var outOfSyncSymbols = symbols.Where(s =>
            {
                var health = healthBySuffix.GetValueOrDefault(s.SqlTableNameSuffix);
                if (health is null || health.LastCandleDate is null || mostRecentSession is null)
                    return true;
                return DateOnly.FromDateTime(health.LastCandleDate.Value) < mostRecentSession.Value;
            }).ToList();

            if (outOfSyncSymbols.Count == 0)
                return Results.Ok(new ReseedSimpleResponse("all_synced", 0));

            var symbolNames = outOfSyncSymbols.Select(s => s.Symbol).ToList();
            var symbolSuffixes = outOfSyncSymbols.Select(s => s.SqlTableNameSuffix).ToList();

            // Check if an HDS reseed is already running.
            var jobRuns = database.GetCollection<BsonDocument>("job_runs");
            var activeFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("job_type", "HDS"),
                Builders<BsonDocument>.Filter.In("outcome", BsonArray.Create(new[] { "running", "started" })));
            var activeRun = await jobRuns.Find(activeFilter).FirstOrDefaultAsync(ct);
            if (activeRun is not null)
            {
                var activeRunId = activeRun["_id"].AsObjectId;
                var activeStartedAt = activeRun.GetValue("started_at", BsonNull.Value) switch
                {
                    BsonDateTime bdt => bdt.ToUniversalTime(),
                    _ => (DateTime?)null
                };
                return Results.Conflict(new HdsConflictResponse(
                    "An HDS job is already running.",
                    activeRunId.ToString(),
                    activeStartedAt?.ToString("o")
                ));
            }

            var now = DateTime.UtcNow;

            // Create the job_runs record. REQ-UNIV-015b: triggered_by: manual_reseed.
            var jobRunDoc = new BsonDocument
            {
                ["job_type"] = "HDS",
                ["trigger_source"] = "manual",
                ["triggered_by"] = "manual_reseed",
                ["scheduled_run_time"] = now,
                ["started_at"] = now,
                ["outcome"] = BsonNull.Value,
                ["symbols_processed"] = outOfSyncSymbols.Count,
                ["symbols"] = BsonArray.Create(symbolNames),
                ["suffixes"] = BsonArray.Create(symbolSuffixes),
                ["created_by"] = adminId,
                ["created_at"] = now,
                ["errors"] = new BsonArray()
            };

            await jobRuns.InsertOneAsync(jobRunDoc, cancellationToken: ct);

            logger.LogInformation(
                "HDS reseed triggered by admin {Admin} for {Count} out-of-sync symbols: {Symbols}",
                adminId, outOfSyncSymbols.Count, string.Join(", ", symbolNames));

            return Results.Ok(new ReseedTriggeredResponse(
                "reseed_triggered",
                jobRunDoc["_id"].AsObjectId.ToString(),
                outOfSyncSymbols.Count,
                symbolNames
            ));
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // GET /api/v1/admin/universe/hds-status — status of the latest HDS job run.
        // REQ-UNIV-015c: in-flight progress for HDS jobs triggered from the upload page.
        admin.MapGet("/hds-status", async (
            IMongoDatabase database,
            CancellationToken ct) =>
        {
            var jobRuns = database.GetCollection<BsonDocument>("job_runs");

            // Find the most recent HDS job triggered by universe_upload or manual_reseed.
            var filterBuilder = Builders<BsonDocument>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq("job_type", "HDS"),
                filterBuilder.In("triggered_by", BsonArray.Create(new[] { "universe_upload", "manual_reseed" })));

            var latest = await jobRuns
                .Find(filter)
                .Sort(Builders<BsonDocument>.Sort.Descending("started_at"))
                .Limit(5)
                .ToListAsync(ct);

            var mapped = latest.Select(r => new HdsRunItem(
                r["_id"].AsObjectId.ToString(),
                r.GetValue("job_type", "").AsString,
                r.GetValue("triggered_by", "").AsString,
                r.GetValue("started_at", BsonNull.Value) switch
                {
                    BsonDateTime bdt => bdt.ToUniversalTime().ToString("o"),
                    _ => null
                },
                r.GetValue("ended_at", BsonNull.Value) switch
                {
                    BsonDateTime bdt => bdt.ToUniversalTime().ToString("o"),
                    _ => null
                },
                r.GetValue("outcome", "").AsString,
                r.GetBsonInt32OrNull("symbols_processed") ?? 0,
                r.GetValue("symbols", BsonNull.Value) switch
                {
                    BsonArray arr => arr.Select(s => s.AsString).ToList(),
                    _ => new List<string>()
                },
                r.GetValue("errors", BsonNull.Value) switch
                {
                    BsonArray arr => arr.Select(e => e.AsString).ToList(),
                    _ => new List<string>()
                }
            )).ToList();

            var currentRun = mapped.FirstOrDefault(r => r.Outcome == "started" || r.Outcome == "running");

            return Results.Ok(new HdsStatusResponse(
                currentRun,
                mapped,
                currentRun is not null
            ));
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // GET /api/v1/admin/universe/work-queue — admin work queue (REQ-UNIV-021b).
        // Returns probe-flagged symbols, FYERS-invalid symbols, and pending rename candidates.
        admin.MapGet("/work-queue", async (
            ISymbolMasterRepository symbolRepo,
            ISysConfigRepository configRepo,
            IUniverseUploadRepository uploadRepo,
            CancellationToken ct) =>
        {
            // Read flag threshold from sys_config.
            var thresholdDoc = await configRepo.GetByKeyAsync(
                "operations.universe.symbol_probe_flag_threshold_days", ct);
            var flagThreshold = thresholdDoc?.GetValue("value", 2) is BsonValue bv && bv.IsInt32
                ? bv.AsInt32 : 2;

            // Check if probe is enabled.
            var enabledDoc = await configRepo.GetByKeyAsync(
                "operations.universe.symbol_probe_enabled", ct);
            var probeEnabled = enabledDoc?.GetValue("value", true) is BsonValue bv2
                && bv2.IsBoolean && bv2.AsBoolean;

            // Get all active symbols — check for probe flags and FYERS-invalid flags.
            var symbols = await symbolRepo.GetAllAsync(archived: false, ct);

            var items = new List<WorkQueueItem>();

            // ── Probe-flagged symbols ────────────────────────────────────────
            foreach (var sym in symbols.Where(s => s.ConsecutiveFailureCount >= flagThreshold
                && s.FyersMarkedInvalidAt is null))
            {
                // Check if this symbol was also flagged as a rename candidate
                // in a recent upload (REQ-UNIV-020 detection).
                var latestUpload = await uploadRepo.GetLatestAsync(ct);
                var hadRenameFlag = latestUpload?.RenameResolutions
                    .Any(r => r.OldSymbol == sym.Symbol) ?? false;

                items.Add(new WorkQueueItem(
                    sym.Id.ToString(),
                    "probe_flag",
                    sym.Symbol,
                    sym.CompanyName,
                    sym.Isin,
                    new WorkQueueItemDetail(
                        sym.ConsecutiveFailureCount,
                        flagThreshold,
                        sym.LastSuccessfulProbeAt?.ToString("o"),
                        sym.LastUnknownSymbolAt?.ToString("o")
                    ),
                    sym.LastUnknownSymbolAt?.ToString("o")
                        ?? sym.UpdatedAt.ToString("o"),
                    hadRenameFlag
                ));
            }

            // ── FYERS-invalid symbols ───────────────────────────────────────
            foreach (var sym in symbols.Where(s => s.FyersMarkedInvalidAt is not null))
            {
                items.Add(new WorkQueueItem(
                    sym.Id.ToString(),
                    "fyers_invalid",
                    sym.Symbol,
                    sym.CompanyName,
                    sym.Isin,
                    new WorkQueueItemDetail(
                        sym.ConsecutiveFailureCount,
                        flagThreshold,
                        sym.LastSuccessfulProbeAt?.ToString("o"),
                        sym.FyersMarkedInvalidAt?.ToString("o")
                    ),
                    sym.FyersMarkedInvalidAt?.ToString("o")
                        ?? sym.UpdatedAt.ToString("o"),
                    highConfidence: true
                ));
            }

            return Results.Ok(new WorkQueueResponse(items, probeEnabled));
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // POST /api/v1/admin/universe/work-queue/{id}/resolve — resolve a probe flag.
        // REQ-UNIV-021b: approve_rename, mark_delisting, or dismiss.
        admin.MapPost("/work-queue/{id}/resolve", async (
            string id,
            WorkQueueResolveRequest request,
            HttpContext context,
            ISymbolMasterRepository symbolRepo,
            IAuditEventRepository auditRepo,
            CancellationToken ct) =>
        {
            var adminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(adminId))
                return Results.Unauthorized();

            if (!ObjectId.TryParse(id, out var objectId))
                return Results.BadRequest(new ErrorResponse("Invalid symbol ID."));

            var symbol = await symbolRepo.FindBySymbolAsync(request.Symbol, ct);
            if (symbol is null)
                return Results.NotFound(new ErrorResponse("Symbol not found."));

            switch (request.Resolution)
            {
                case "approve_rename":
                    if (string.IsNullOrWhiteSpace(request.NewSymbol))
                        return Results.BadRequest(new ErrorResponse("new_symbol is required for approve_rename."));

                    await symbolRepo.RenameSymbolAsync(symbol.Id, request.NewSymbol, ct);
                    await symbolRepo.ClearFyersInvalidAsync(symbol.Id, ct);

                    await auditRepo.RecordAsync(adminId, "work_queue_resolve", DateTime.UtcNow,
                        new Dictionary<string, object?>
                        {
                            ["resolution"] = "approve_rename",
                            ["symbol"] = request.Symbol,
                            ["new_symbol"] = request.NewSymbol,
                            ["previous_failure_count"] = symbol.ConsecutiveFailureCount,
                            ["cleared_fyers_invalid"] = symbol.FyersMarkedInvalidAt is not null
                        }, ct);

                    return Results.Ok(new ResolveRenameResponse("renamed", request.Symbol, request.NewSymbol!));

                case "mark_delisting":
                    await symbolRepo.UpdateMetadataAsync(symbol.Id, isArchived: true, ct: ct);
                    await symbolRepo.ClearFyersInvalidAsync(symbol.Id, ct);

                    await auditRepo.RecordAsync(adminId, "work_queue_resolve", DateTime.UtcNow,
                        new Dictionary<string, object?>
                        {
                            ["resolution"] = "mark_delisting",
                            ["symbol"] = request.Symbol,
                            ["reason"] = request.Reason ?? "",
                            ["previous_failure_count"] = symbol.ConsecutiveFailureCount,
                            ["cleared_fyers_invalid"] = symbol.FyersMarkedInvalidAt is not null
                        }, ct);

                    // REQ-UNIV-015: notify users with open positions in archived symbols.
                    var notifDbSymbol = context.RequestServices.GetRequiredService<IMongoDatabase>();
                    var notifWriterSymbol = context.RequestServices.GetRequiredService<INotificationWriter>();
                    var notifLoggerSymbol = context.RequestServices.GetRequiredService<ILogger<UniverseSyncService>>();
                    await NotifyArchivedSymbolOpenPositionsAsync(
                        [request.Symbol], notifDbSymbol, notifWriterSymbol, notifLoggerSymbol, ct);

                    return Results.Ok(new ResolveResponse("archived", request.Symbol));

                case "dismiss":
                    // Dismiss: clear the FYERS-invalid flag but leave the symbol
                    // active. Only clear consecutive_failure_count if a FYERS
                    // invalid flag is being cleared (the probe system manages its
                    // own count separately).
                    if (symbol.FyersMarkedInvalidAt is not null)
                    {
                        await symbolRepo.ClearFyersInvalidAsync(symbol.Id, ct);
                    }

                    await auditRepo.RecordAsync(adminId, "work_queue_resolve", DateTime.UtcNow,
                        new Dictionary<string, object?>
                        {
                            ["resolution"] = "dismiss",
                            ["symbol"] = request.Symbol,
                            ["reason"] = request.Reason ?? "",
                            ["cleared_fyers_invalid"] = symbol.FyersMarkedInvalidAt is not null,
                            ["failure_count_not_reset"] = true
                        }, ct);

                    return Results.Ok(new ResolveResponse("dismissed", request.Symbol));

                default:
                    return Results.BadRequest(new ErrorResponse(
                        "Invalid resolution. Must be 'approve_rename', 'mark_delisting', or 'dismiss'."
                    ));
            }
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        // POST /api/v1/admin/universe/probe — manually trigger a symbol validity probe.
        // REQ-UNIV-021: manual trigger path.
        admin.MapPost("/probe", async (
            HttpContext context,
            SymbolProbeService probeService,
            CancellationToken ct) =>
        {
            var adminId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(adminId))
                return Results.Unauthorized();

            var summary = await probeService.RunProbeAsync("manual", adminId, ct);

            if (summary is null)
                return Results.Ok(new ProbeDisabledResponse(
                    "disabled",
                    "Symbol probe is disabled. Enable via operations.universe.symbol_probe_enabled in sys_config."
                ));

            return Results.Ok(new ProbeCompletedResponse(
                "completed",
                summary.JobRunId!,
                summary.TotalSymbols,
                summary.SuccessCount,
                summary.TransientCount,
                summary.UnknownCount
            ));
        })
        .RequireAuthorization(policy => policy.RequireRole("admin"))
        .WithTags(Tag);

        return app;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// For each archived symbol, checks whether any user has an open position
    /// and generates a <c>symbol_archived_with_open_position</c> notification.
    /// REQ-UNIV-015.
    /// </summary>
    private static async Task NotifyArchivedSymbolOpenPositionsAsync(
        IReadOnlyList<string> archivedSymbols,
        IMongoDatabase database,
        INotificationWriter notificationWriter,
        ILogger<UniverseSyncService> logger,
        CancellationToken ct)
    {
        if (archivedSymbols.Count == 0) return;

        try
        {
            var positionsCollection = database.GetCollection<BsonDocument>("positions");
            var nonTerminalStates = new[] { "PendingEntry", "Open", "Suspended" };

            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.In("symbol",
                    BsonArray.Create(archivedSymbols.ToArray())),
                Builders<BsonDocument>.Filter.In("state",
                    BsonArray.Create(nonTerminalStates)));

            var positions = await positionsCollection.Find(filter).ToListAsync(ct);
            if (positions.Count == 0) return;

            // Group by user.
            var userPositions = positions
                .GroupBy(p => p.GetBsonStringOrNull("user_id") ?? "")
                .Where(g => !string.IsNullOrWhiteSpace(g.Key))
                .ToList();

            if (userPositions.Count == 0) return;

            var notifications = new List<NotificationDocument>();
            var now = DateTime.UtcNow;

            foreach (var userGroup in userPositions)
            {
                var symbols = userGroup
                    .Select(p => p.GetValue("symbol", "")?.AsString ?? "")
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var symbol in symbols)
                {
                    if (!ObjectId.TryParse(userGroup.Key, out var userId))
                        continue;

                    notifications.Add(new NotificationDocument
                    {
                        UserId = userId,
                        NotificationType = NotificationType.SymbolArchivedWithOpenPosition,
                        Symbol = symbol,
                        Content = $"The symbol {symbol} has been removed from the Nifty 500 universe. " +
                                  "Your position will no longer receive new entry signal advisories " +
                                  "but will continue to be monitored by the RME for stop updates, " +
                                  "add and reduce level triggers, and exit conditions until you close the position.",
                        DeepLink = $"/chart?symbol={symbol}",
                        GeneratedAt = now,
                        IsAdminNotification = false,
                        IsRead = false
                    });
                }
            }

            if (notifications.Count > 0)
            {
                await notificationWriter.WriteBatchAsync(notifications, ct);
                logger.LogInformation(
                    "Generated {Count} symbol_archived_with_open_position notifications for symbols: {Symbols}",
                    notifications.Count, string.Join(", ", archivedSymbols));
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to generate symbol_archived_with_open_position notifications " +
                "for archived symbols: {Symbols}. Non-fatal; archive was committed.",
                string.Join(", ", archivedSymbols));
        }
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

/// <summary>Request body for POST /api/v1/admin/universe/work-queue/{id}/resolve.</summary>
public sealed record WorkQueueResolveRequest
{
    /// <summary>The symbol value to resolve.</summary>
    public required string Symbol { get; init; }

    /// <summary>Resolution action: <c>approve_rename</c>, <c>mark_delisting</c>, or <c>dismiss</c>.</summary>
    public required string Resolution { get; init; }

    /// <summary>New symbol value (required for approve_rename).</summary>
    public string? NewSymbol { get; init; }

    /// <summary>Operator-supplied reason (required for dismiss, optional for others).</summary>
    public string? Reason { get; init; }
}

// ── Response Records ───────────────────────────────────────────────────────

internal sealed record HdsConflictResponse(string Error, string JobRunId, string? StartedAt);

// Upload preview
internal sealed record PreviewAddEntry(string Symbol, string CompanyName, string Industry, string Isin, string SqlTableNameSuffix);
internal sealed record PreviewArchiveEntry(string Symbol, string CompanyName, string Isin);
internal sealed record PreviewIndustryChange(string Symbol, string PreviousIndustry, string NewIndustry);
internal sealed record PreviewSkippedEntry(string Symbol, string CompanyName, string Series);
internal sealed record PreviewRenameCandidate(string OldSymbol, string NewSymbol, string Isin, string CompanyName, string Industry);
internal sealed record UploadPreviewResponse(string FileHash, int TotalRows, int AcceptedRows, List<PreviewAddEntry> Adds, List<PreviewArchiveEntry> Archives, List<PreviewIndustryChange> IndustryChanges, List<PreviewSkippedEntry> Skipped, List<PreviewRenameCandidate> RenameCandidates);

// Upload commit
internal sealed record UploadCommitResponse(string UploadId, List<string> NewSymbols, List<string> ArchivedSymbols, bool HdsTriggered, string? HdsJobRunId);

// Upload list
internal sealed record UploadListItem(string Id, string UploadedBy, DateTime UploadedAt, string FileHash, int TotalRows, int RowsAccepted, int RowsArchived, int RowsExcluded, List<string> NewSymbols, List<string> ArchivedSymbols, bool HdsTriggered, bool RolledBack, DateTime? RolledBackAt, string? RolledBackBy);
internal sealed record UploadListResponse(List<UploadListItem> Uploads);

// Rollback
internal sealed record UploadRollbackResponse(string Status, string UploadId);

// Sync health
internal sealed record SyncHealthEmptyResponse(object[] Symbols, int OutOfSyncCount);
internal sealed record SymbolHealthItem(string Symbol, string CompanyName, bool IsArchived, bool ScanExcluded, string SqlTableNameSuffix, string? LastCandleDate, string? MostRecentSession, bool OutOfSync);
internal sealed record SyncHealthResponse(List<SymbolHealthItem> Symbols, int OutOfSyncCount, int TotalActive, string? MostRecentSession);

// Reseed
internal sealed record ReseedSimpleResponse(string Status, int SymbolsReseeded);
internal sealed record ReseedTriggeredResponse(string Status, string JobRunId, int SymbolsReseeded, List<string> Symbols);

// HDS status
internal sealed record HdsRunItem(string Id, string JobType, string TriggeredBy, string? StartedAt, string? EndedAt, string Outcome, int SymbolsProcessed, List<string> Symbols, List<string> Errors);
internal sealed record HdsStatusResponse(HdsRunItem? CurrentRun, List<HdsRunItem> RecentRuns, bool IsRunning);

// Work queue
internal sealed record WorkQueueItemDetail(int ConsecutiveFailureCount, int FlagThreshold, string? LastSuccessfulProbeAt, string? LastUnknownSymbolAt);
internal sealed record WorkQueueItem(string Id, string Type, string Symbol, string CompanyName, string Isin, WorkQueueItemDetail Details, string? CreatedAt, bool HighConfidence);
internal sealed record WorkQueueResponse(List<WorkQueueItem> Items, bool ProbeEnabled);

// Work queue resolve
internal sealed record ResolveResponse(string Status, string Symbol);
internal sealed record ResolveRenameResponse(string Status, string Symbol, string NewSymbol);

// Probe
internal sealed record ProbeDisabledResponse(string Status, string Message);
internal sealed record ProbeCompletedResponse(string Status, string JobRunId, int TotalSymbols, int SuccessCount, int TransientCount, int UnknownCount);
