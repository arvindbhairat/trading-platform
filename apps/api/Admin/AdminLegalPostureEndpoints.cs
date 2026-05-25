using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Auth;
using SignalStack.Storage.SysConfig;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin Legal Posture widget endpoint (P8-T4 / REQ-LEGAL-010).
/// Returns operating-phase context, document versions, SEBI opinion status,
/// legal-review gate status, and runbook catalog stats for the admin home page.
/// </summary>
public static class AdminLegalPostureEndpoints
{
    private const int TotalRunbookCatalog = 18;

    private static readonly string[] RunbookRelativePaths =
    [
        Path.Combine("..", "..", "docs", "operations", "runbooks"),
        Path.Combine("..", "..", "..", "..", "..", "..", "docs", "operations", "runbooks"),
    ];

    public static IEndpointRouteBuilder MapAdminLegalPostureEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/legal-posture
        // Legal Posture widget data (REQ-LEGAL-010).
        admin.MapGet("/legal-posture", async (
            HttpContext context,
            IUserRepository userRepo,
            ISysConfigRepository configRepo,
            IMongoDatabase database) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var ct = context.RequestAborted;

            // 1. Current operating phase.
            var currentPhase = await configRepo.GetCurrentPhaseAsync(ct);

            // 2. Approved active user count and tester ceiling.
            var approvedUserCount = await userRepo.CountApprovedActiveAsync(ct);
            var testerCeiling = await configRepo.GetTesterCeilingAsync(ct);

            // 3. FYERS app type (personal vs commercial).
            var commercialFyers = await configRepo.GetByKeyAsync("legal.phase_b.commercial_fyers_configured", ct);
            var isCommercialFyers = commercialFyers?.GetValue("value", false).AsBoolean ?? false;

            // 4. Legal document versions.
            var tosVersion = await GetConfigStringAsync(database, "legal.tos.current_version", "v1", ct);
            var privacyVersion = await GetConfigStringAsync(database, "legal.privacy.current_version", "v1", ct);
            var disclaimerVersion = await GetConfigStringAsync(database, "legal.disclaimer.long_version", "v1", ct);
            var testerAckVersion = await GetConfigStringAsync(database, "legal.tester_acknowledgement.current_version", "v1", ct);

            // 5. SEBI classification opinion status.
            var sebiOpinionReceived = await configRepo.GetByKeyAsync("legal.sebi_opinion.received", ct);
            var sebiReceived = sebiOpinionReceived?.GetValue("value", false).AsBoolean ?? false;
            var sebiDateDoc = await configRepo.GetByKeyAsync("legal.sebi_opinion.received_date", ct);
            var sebiReceivedDate = sebiDateDoc?.GetValue("value", BsonNull.Value)?.AsString;

            // 6. Legal-review gate status.
            var legalReviewGate = await configRepo.GetByKeyAsync("legal.phase_b.legal_review_received", ct);
            var legalReviewReceived = legalReviewGate?.GetValue("value", false).AsBoolean ?? false;

            // 7. Runbook catalog stats.
            var (authoredCount, _) = GetAuthoredRunbookCount();
            var reviewedIn90Days = await CountRunbookReviewsAsync(database, ct);

            return Results.Ok(new
            {
                current_phase = currentPhase,
                approved_user_count = approvedUserCount,
                tester_ceiling = testerCeiling,
                fyers_app_type = isCommercialFyers ? "commercial" : "personal",
                tos_version = tosVersion,
                privacy_version = privacyVersion,
                disclaimer_version = disclaimerVersion,
                tester_acknowledgement_version = testerAckVersion,
                sebi_opinion = new
                {
                    received = sebiReceived,
                    received_date = string.IsNullOrEmpty(sebiReceivedDate) ? null : sebiReceivedDate,
                },
                legal_review_received = legalReviewReceived,
                runbook_catalog = new
                {
                    total = TotalRunbookCatalog,
                    authored = authoredCount,
                    reviewed_in_90_days = reviewedIn90Days,
                },
            });
        }).RequireAuthorization();

        return app;
    }

    private static async Task<string> GetConfigStringAsync(
        IMongoDatabase database, string key, string defaultValue, CancellationToken ct)
    {
        var col = database.GetCollection<BsonDocument>("sys_config");
        var doc = await col.Find(Builders<BsonDocument>.Filter.Eq("key", key))
            .FirstOrDefaultAsync(ct);
        var value = doc?.GetValue("value", BsonNull.Value);
        return value?.IsString == true ? value.AsString : defaultValue;
    }

    /// <summary>
    /// Counts authored runbook files (non-README markdown) in the docs/operations/runbooks/ directory.
    /// Returns (count, list_of_filenames).
    /// </summary>
    private static (int count, List<string> files) GetAuthoredRunbookCount()
    {
        var baseDir = Directory.GetCurrentDirectory();
        string? runbookDir = null;

        foreach (var relative in RunbookRelativePaths)
        {
            var candidate = Path.GetFullPath(Path.Combine(baseDir, relative));
            if (Directory.Exists(candidate))
            {
                runbookDir = candidate;
                break;
            }
        }

        if (runbookDir is null)
            return (0, []);

        var files = Directory.GetFiles(runbookDir, "*.md")
            .Select(Path.GetFileName)
            .OfType<string>()
            .Where(f => !string.Equals(f, "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f)
            .ToList();

        return (files.Count, files);
    }

    /// <summary>
    /// Counts runbook review audit events within the past 90 days.
    /// Matches action_type values starting with "runbook_reviewed" via regex.
    /// </summary>
    private static async Task<int> CountRunbookReviewsAsync(
        IMongoDatabase database, CancellationToken ct)
    {
        var ninetyDaysAgo = DateTime.UtcNow.AddDays(-90);
        var col = database.GetCollection<BsonDocument>("audit_events");
        var filter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Regex("action_type",
                new BsonRegularExpression("^runbook_reviewed", "i")),
            Builders<BsonDocument>.Filter.Gte("event_at", ninetyDaysAgo));

        var count = await col.CountDocumentsAsync(filter, cancellationToken: ct);
        return (int)count;
    }

    private static async Task<bool> IsAdminAsync(HttpContext context, IUserRepository userRepo)
    {
        var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";

        if (string.IsNullOrEmpty(userId))
            return false;

        var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
        return user is not null && user.Role == UserRole.Admin;
    }
}
