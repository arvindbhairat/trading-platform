using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Fyers;
using SignalStack.Api.Users;

namespace SignalStack.Api.Admin;

public static class AdminHomeEndpoints
{
    public static IEndpointRouteBuilder MapAdminHomeEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/degraded-mode
        // Returns the current shared-ingestion token health state for the admin
        // banner (REQ-MARKET-016). Reads from sys_config, which is updated by
        // the worker's SharedTokenHealthService when the token degrades/recovers.
        admin.MapGet("/degraded-mode", async (
            IMongoDatabase database) =>
        {
            var sysConfig = database.GetCollection<BsonDocument>("sys_config");

            var degradedDoc = await sysConfig
                .Find(Builders<BsonDocument>.Filter.Eq("_id", "ops.degraded_mode.shared_ingestion_token"))
                .FirstOrDefaultAsync();

            var isDegraded = degradedDoc?.GetValue("value", BsonNull.Value)?.AsString == "true";

            if (!isDegraded)
            {
                return Results.Ok(new DegradedModeResponse(
                    IsDegraded: false,
                    BannerMessage: null,
                    LastSuccessfulTokenAt: null,
                    DegradedReason: null));
            }

            var lastTokenDoc = await sysConfig
                .Find(Builders<BsonDocument>.Filter.Eq("_id", "ops.degraded_mode.last_successful_token_at"))
                .FirstOrDefaultAsync();

            var reasonDoc = await sysConfig
                .Find(Builders<BsonDocument>.Filter.Eq("_id", "ops.degraded_mode.degraded_reason"))
                .FirstOrDefaultAsync();

            var lastTokenAt = lastTokenDoc?.GetValue("value", BsonNull.Value)?.AsString;
            var degradedReason = reasonDoc?.GetValue("value", BsonNull.Value)?.AsString;

            var bannerMessage =
                "Shared-ingestion suspended: admin FYERS token refresh failed — " +
                "LMDS and DataSync are halted. " +
                (lastTokenAt is not null
                    ? $"Last successful token at {lastTokenAt}. "
                    : "") +
                "Re-authenticate to restore.";

            return Results.Ok(new DegradedModeResponse(
                IsDegraded: true,
                BannerMessage: bannerMessage,
                LastSuccessfulTokenAt: lastTokenAt,
                DegradedReason: degradedReason));
        });

        // GET /api/v1/admin/transfer-recovery-summary
        // Returns a summary of failed/skipped background job runs during the
        // admin transfer window (REQ-ROLE-007a). The transfer window is defined
        // as the period from the last successful job run before the admin transfer
        // restart to the current admin's first login.
        // Returns empty data when no transfer recovery is needed.
        admin.MapGet("/transfer-recovery-summary", async (
            HttpContext context,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo,
            IMongoDatabase database) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            // Check if the summary has already been dismissed by this admin.
            var dismissed = await auditRepo.HasEventAsync(
                userId, "transfer_recovery_dismissed", context.RequestAborted);
            if (dismissed)
                return Results.Ok(new TransferRecoverySummaryResponse(
                    NeedsReview: false,
                    JobCount: 0,
                    FailedJobs: [],
                    TokenMissing: false,
                    FyersAuthUrl: null,
                    Dismissed: true));

            // Query the job_runs collection for failed/skipped runs.
            var jobRuns = database.GetCollection<JobRunDocument>("job_runs");

            // Determine transfer window: find the most recent successful job run
            // to establish the window start. If none exists, use a 24h window.
            var lastSuccessFilter = Builders<JobRunDocument>.Filter
                .Eq(j => j.Outcome, "success");
            var lastSuccess = await jobRuns
                .Find(lastSuccessFilter)
                .SortByDescending(j => j.StartedAt)
                .FirstOrDefaultAsync(context.RequestAborted);

            var windowStart = lastSuccess?.StartedAt
                ?? DateTime.UtcNow.AddHours(-24);

            // Find failed/skipped runs in the transfer window.
            var failedFilter = Builders<JobRunDocument>.Filter.And(
                Builders<JobRunDocument>.Filter.Gte(j => j.StartedAt, windowStart),
                Builders<JobRunDocument>.Filter.In(j => j.Outcome, ["failed", "skipped"]));

            var failedJobs = await jobRuns
                .Find(failedFilter)
                .SortByDescending(j => j.ScheduledRunTime)
                .ToListAsync(context.RequestAborted);

            // Check admin FYERS token status.
            var fyersTokens = database.GetCollection<FyersTokenDocument>("fyers_tokens");
            var adminTokenFilter = Builders<FyersTokenDocument>.Filter.And(
                Builders<FyersTokenDocument>.Filter.Eq(t => t.UserId, userId),
                Builders<FyersTokenDocument>.Filter.Eq(t => t.Status, FyersTokenStatus.Active));

            var hasAdminToken = await fyersTokens
                .Find(adminTokenFilter)
                .AnyAsync(context.RequestAborted);

            var fyersAuthUrl = !hasAdminToken
                ? $"/api/v1/fyers/auth/init"
                : null;

            var mapped = failedJobs.Select(j => new FailedJobEntry(
                JobType: j.JobType,
                ScheduledRunTime: j.ScheduledRunTime.ToString("o"),
                Outcome: j.Outcome,
                Errors: j.Errors ?? [],
                RunId: j.Id.ToString()
            )).ToList();

            return Results.Ok(new TransferRecoverySummaryResponse(
                NeedsReview: failedJobs.Count > 0 || !hasAdminToken,
                JobCount: failedJobs.Count,
                FailedJobs: mapped,
                TokenMissing: !hasAdminToken,
                FyersAuthUrl: fyersAuthUrl,
                Dismissed: false
            ));
        }).RequireAuthorization();

        // POST /api/v1/admin/transfer-recovery-summary/dismiss
        // Records that the new admin has reviewed and dismissed the transfer
        // recovery summary. The dismissal is recorded in audit_events so the
        // summary is not shown again. (REQ-ROLE-007a)
        admin.MapPost("/transfer-recovery-summary/dismiss", async (
            HttpContext context,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            await auditRepo.RecordAsync(
                userId,
                "transfer_recovery_dismissed",
                DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["dismissed_at"] = DateTime.UtcNow.ToString("o"),
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new { dismissed = true });
        }).RequireAuthorization();

        return app;
    }
}

public sealed record TransferRecoverySummaryResponse(
    bool NeedsReview,
    int JobCount,
    List<FailedJobEntry> FailedJobs,
    bool TokenMissing,
    string? FyersAuthUrl,
    bool Dismissed
);

public sealed record FailedJobEntry(
    string JobType,
    string ScheduledRunTime,
    string Outcome,
    List<string> Errors,
    string RunId
);

public sealed record DegradedModeResponse(
    bool IsDegraded,
    string? BannerMessage,
    string? LastSuccessfulTokenAt,
    string? DegradedReason
);
