using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;

namespace SignalStack.Api.Admin;

/// <summary>
/// Admin system health dashboard endpoints (REQ-ADMIN-014 / A-14).
/// P8-T2: job monitoring, retry controls, Symbol Validity Probe banner, maintenance window display.
/// </summary>
public static class AdminSystemHealthEndpoints
{
    private static readonly string[] BackgroundJobTypes = ["DS", "HDS", "EODSR", "LMDS", "LADS", "NDJ"];

    private const string LookbackDaysKey = "operations.system_health.lookback_days";
    private const string KillSwitchKey = "risk.kill_switch.active";
    private const string ProviderKey = "market_data.provider.active";

    public static IEndpointRouteBuilder MapAdminSystemHealthEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin");

        // GET /api/v1/admin/system-health — full system health report
        // REQ-ADMIN-014: traffic-light status per component, 7-day daily breakdown,
        // all data sourced from existing collections.
        admin.MapGet("/system-health", async (
            HttpContext context,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var now = DateTime.UtcNow;
            var lookbackDoc = await database.GetCollection<BsonDocument>("sys_config")
                .Find(Builders<BsonDocument>.Filter.Eq("key", LookbackDaysKey))
                .FirstOrDefaultAsync(context.RequestAborted);
            var lookbackDays = lookbackDoc?.GetValue("value", 7).IsInt32 == true
                ? lookbackDoc["value"].AsInt32
                : 7;
            var since = now.AddDays(-lookbackDays);

            var jobRunsCol = database.GetCollection<BsonDocument>("job_runs");
            var auditCol = database.GetCollection<BsonDocument>("audit_events");
            var notificationsCol = database.GetCollection<BsonDocument>("notifications");
            var sysConfigCol = database.GetCollection<BsonDocument>("sys_config");

            // ── 1. Background job components ──────────────────────────────────
            var jobComponents = new List<object>();
            foreach (var jobType in BackgroundJobTypes)
            {
                var breakdown = await GetDailyBreakdownAsync(
                    jobRunsCol, jobType, since, now, context.RequestAborted);

                var lastRun = await jobRunsCol
                    .Find(Builders<BsonDocument>.Filter.Eq("job_type", jobType))
                    .SortByDescending(j => j["started_at"])
                    .FirstOrDefaultAsync(context.RequestAborted);

                jobComponents.Add(new
                {
                    name = jobType,
                    label = JobTypeLabel(jobType),
                    status = ComputeJobStatus(lastRun, breakdown),
                    last_run_at = lastRun?.GetValue("started_at", BsonNull.Value)?.ToNullableUniversalTime(),
                    last_outcome = lastRun?.GetValue("outcome", "")?.AsString ?? "never_run",
                    daily_breakdown = breakdown
                });
            }

            // ── 2. Admin FYERS token ─────────────────────────────────────────
            var adminFyers = await BuildAdminFyersTokenAsync(database, auditCol, context.RequestAborted);

            // ── 3. Market data provider ──────────────────────────────────────
            var mdpData = await BuildMarketDataProviderAsync(
                sysConfigCol, jobRunsCol, since, now, context.RequestAborted);

            // ── 4. Telegram delivery pipeline ────────────────────────────────
            var telegramData = await BuildTelegramPipelineAsync(
                notificationsCol, since, now, context.RequestAborted);

            // ── 5. Kill switch ───────────────────────────────────────────────
            var killSwitchData = await BuildKillSwitchAsync(
                sysConfigCol, auditCol, context.RequestAborted);

            // ── 6. Market halt ───────────────────────────────────────────────
            var marketHaltData = await BuildMarketHaltAsync(
                auditCol, since, now, context.RequestAborted);

            // ── 7. Maintenance windows (A-14) ────────────────────────────────
            var maintenanceWindows = new
            {
                pre_market = new { start = "07:00", end = "08:30", timezone = "IST", label = "Pre-market — Universe Sync and small HDS seeds" },
                post_eod = new { start = "20:00", end = "23:59", timezone = "IST", label = "Post-EODSR — large HDS seeds and high-write operations" }
            };

            return Results.Ok(new
            {
                components = jobComponents,
                admin_fyers_token = adminFyers,
                market_data_provider = mdpData,
                telegram_pipeline = telegramData,
                kill_switch = killSwitchData,
                market_halt = marketHaltData,
                maintenance_windows = maintenanceWindows
            });
        }).RequireAuthorization();

        // POST /api/v1/admin/jobs/{jobType}/trigger-manual — trigger a background job run
        // P8-T2: per-job retry / manual trigger. Creates a job_runs record with trigger_source=manual
        // so downstream processors can pick it up (where applicable).
        admin.MapPost("/jobs/{jobType}/trigger-manual", async (
            string jobType,
            HttpContext context,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo,
            IMongoDatabase database) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            if (!BackgroundJobTypes.Contains(jobType) && jobType != "SYMBOL_PROBE")
                return Results.BadRequest(new { error = "unknown_job_type", jobType });

            var userId = context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
                ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? "";

            var now = DateTime.UtcNow;
            var jobRunsCol = database.GetCollection<BsonDocument>("job_runs");

            var jobRunDoc = new BsonDocument
            {
                ["job_type"] = jobType,
                ["trigger_source"] = "manual",
                ["scheduled_run_time"] = now,
                ["started_at"] = now,
                ["ended_at"] = BsonNull.Value,
                ["outcome"] = "pending",
                ["triggered_by"] = $"admin:{userId}",
                ["created_by"] = userId
            };

            await jobRunsCol.InsertOneAsync(jobRunDoc, cancellationToken: context.RequestAborted);

            // Write audit event.
            await auditRepo.RecordAsync(
                userId,
                "job_manual_trigger",
                now,
                details: new Dictionary<string, object?>
                {
                    ["job_type"] = jobType,
                    ["run_id"] = jobRunDoc["_id"].AsObjectId.ToString()
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new
            {
                triggered = true,
                job_type = jobType,
                run_id = jobRunDoc["_id"].AsObjectId.ToString()
            });
        }).RequireAuthorization();

        // GET /api/v1/admin/symbol-probe/summary — latest Symbol Validity Probe results
        // P8-T2: Symbol Validity Probe banner data on admin home.
        admin.MapGet("/symbol-probe/summary", async (
            HttpContext context,
            IUserRepository userRepo,
            IMongoDatabase database) =>
        {
            if (!await IsAdminAsync(context, userRepo))
                return Results.Forbid();

            var jobRunsCol = database.GetCollection<BsonDocument>("job_runs");
            var lastProbe = await jobRunsCol
                .Find(Builders<BsonDocument>.Filter.Eq("job_type", "SYMBOL_PROBE"))
                .SortByDescending(j => j["started_at"])
                .FirstOrDefaultAsync(context.RequestAborted);

            if (lastProbe is null)
            {
                return Results.Ok(new SymbolProbeSummaryResponse(
                    LastRunAt: null,
                    TotalSymbols: 0,
                    FlaggedCount: 0,
                    FlaggedSymbols: [],
                    Outcome: null,
                    ShowBanner: false));
            }

            var flaggedSymbols = lastProbe.GetValue("flagged_symbols", BsonNull.Value) switch
            {
                BsonArray arr => arr.Select(v => v.AsString).ToList(),
                _ => []
            };

            // Check banner suppression flag.
            var sysConfigCol = database.GetCollection<BsonDocument>("sys_config");
            var suppressDoc = await sysConfigCol
                .Find(Builders<BsonDocument>.Filter.Eq("key", "operations.system_health.svp_banner_suppressed"))
                .FirstOrDefaultAsync(context.RequestAborted);
            var suppressBanner = suppressDoc?.GetValue("value", false) is BsonBoolean bSuppress && bSuppress.Value;

            var thresholdDoc = await sysConfigCol
                .Find(Builders<BsonDocument>.Filter.Eq("key", "operations.symbol_probe.flag_threshold_for_banner"))
                .FirstOrDefaultAsync(context.RequestAborted);
            var flagThreshold = thresholdDoc?.GetValue("value", 5).IsInt32 == true
                ? thresholdDoc["value"].AsInt32
                : 5;

            var showBanner = !suppressBanner && flaggedSymbols.Count >= flagThreshold;

            return Results.Ok(new SymbolProbeSummaryResponse(
                LastRunAt: lastProbe.GetValue("started_at", BsonNull.Value).ToNullableUniversalTime(),
                TotalSymbols: lastProbe.GetValue("symbols_processed", 0).AsInt32,
                FlaggedCount: flaggedSymbols.Count,
                FlaggedSymbols: flaggedSymbols,
                Outcome: lastProbe.GetValue("outcome", "")?.AsString ?? "",
                ShowBanner: showBanner));
        }).RequireAuthorization();

        return app;
    }

    // ── Per-job daily breakdown ──────────────────────────────────────────────

    private static async Task<List<object>> GetDailyBreakdownAsync(
        IMongoCollection<BsonDocument> jobRunsCol,
        string jobType,
        DateTime since,
        DateTime now,
        CancellationToken ct)
    {
        var pipeline = new BsonDocument[]
        {
            new()
            {
                ["$match"] = new BsonDocument
                {
                    ["job_type"] = jobType,
                    ["started_at"] = new BsonDocument
                    {
                        ["$gte"] = since,
                        ["$lte"] = now
                    }
                }
            },
            new()
            {
                ["$group"] = new BsonDocument
                {
                    ["_id"] = new BsonDocument
                    {
                        ["$dateToString"] = new BsonDocument
                        {
                            ["format"] = "%Y-%m-%d",
                            ["date"] = "$started_at"
                        }
                    },
                    ["runs"] = new BsonDocument("$sum", 1),
                    ["success"] = new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$outcome", "success" }), 1, 0
                    })),
                    ["warnings"] = new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$in", new BsonArray { "$outcome", new BsonArray { "partial", "skipped" } }), 1, 0
                    })),
                    ["errors"] = new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$outcome", "failed" }), 1, 0
                    }))
                }
            },
            new() { ["$sort"] = new BsonDocument("_id", 1) }
        };

        var results = await jobRunsCol.Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .ToListAsync(ct);

        return results.Select(r => (object)new
        {
            date = r["_id"].AsString,
            runs = r.GetValue("runs", 0).AsInt32,
            success = r.GetValue("success", 0).AsInt32,
            warnings = r.GetValue("warnings", 0).AsInt32,
            errors = r.GetValue("errors", 0).AsInt32
        }).ToList();
    }

    // ── Admin FYERS token ────────────────────────────────────────────────────

    private static async Task<object> BuildAdminFyersTokenAsync(
        IMongoDatabase database,
        IMongoCollection<BsonDocument> auditCol,
        CancellationToken ct)
    {
        // Find the admin user's FYERS token.
        var fyersTokens = database.GetCollection<BsonDocument>("fyers_tokens");
        var adminToken = await fyersTokens
            .Find(Builders<BsonDocument>.Filter.Eq("status", "Active"))
            .FirstOrDefaultAsync(ct);

        string tokenStatus;
        DateTime? expiry = null;
        if (adminToken is null)
        {
            tokenStatus = "missing";
        }
        else
        {
            var expiresAt = adminToken.GetValue("expires_at", BsonNull.Value);
            if (expiresAt.IsBsonNull)
            {
                tokenStatus = "valid";
            }
            else
            {
                expiry = expiresAt.ToUniversalTime();
                tokenStatus = expiry > DateTime.UtcNow ? "valid" : "expired";
            }
        }

        // 7-day history of token validity events from audit_events.
        var since = DateTime.UtcNow.AddDays(-7);
        var tokenEvents = await auditCol
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Regex("action_type", new BsonRegularExpression("fyers_token|token_refresh|token_dirty")),
                Builders<BsonDocument>.Filter.Gte("event_at", since)))
            .SortByDescending(e => e["event_at"])
            .Limit(20)
            .ToListAsync(ct);

        var history = tokenEvents.Select(e => (object)new
        {
            event_at = e.GetValue("event_at", BsonNull.Value).ToNullableUniversalTime(),
            action_type = e.GetValue("action_type", "")?.AsString ?? "",
            details = e.GetValue("details", BsonNull.Value).IsBsonNull ? null : e["details"]
        }).ToList();

        return new
        {
            status = tokenStatus,
            expires_at = expiry,
            history
        };
    }

    // ── Market data provider ─────────────────────────────────────────────────

    private static async Task<object> BuildMarketDataProviderAsync(
        IMongoCollection<BsonDocument> sysConfigCol,
        IMongoCollection<BsonDocument> jobRunsCol,
        DateTime since,
        DateTime now,
        CancellationToken ct)
    {
        var providerDoc = await sysConfigCol
            .Find(Builders<BsonDocument>.Filter.Eq("key", ProviderKey))
            .FirstOrDefaultAsync(ct);
        var activeProvider = providerDoc?.GetValue("value", "fyers")?.AsString ?? "fyers";

        // Last successful data fetch from DS or HDS job runs.
        var lastSuccess = await jobRunsCol
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.In("job_type", new BsonArray { "DS", "HDS" }),
                Builders<BsonDocument>.Filter.Eq("outcome", "success")))
            .SortByDescending(j => j["started_at"])
            .FirstOrDefaultAsync(ct);

        var breakdown = await GetDailyBreakdownAsync(
            jobRunsCol, "DS", since, now, ct);
        var hdsBreakdown = await GetDailyBreakdownAsync(
            jobRunsCol, "HDS", since, now, ct);

        // Combine DS + HDS daily breakdown.
        var combined = breakdown.Concat(hdsBreakdown)
            .GroupBy(d => ((dynamic)d).date)
            .Select(g => new
            {
                date = g.Key,
                runs = g.Sum(d => ((dynamic)d).runs),
                success = g.Sum(d => ((dynamic)d).success),
                warnings = g.Sum(d => ((dynamic)d).warnings),
                errors = g.Sum(d => ((dynamic)d).errors)
            })
            .OrderBy(d => d.date)
            .ToList();

        return new
        {
            active_provider = activeProvider,
            last_successful_fetch_at = lastSuccess?.GetValue("started_at", BsonNull.Value)?.ToNullableUniversalTime(),
            daily_breakdown = combined
        };
    }

    // ── Telegram pipeline ────────────────────────────────────────────────────

    private static async Task<object> BuildTelegramPipelineAsync(
        IMongoCollection<BsonDocument> notificationsCol,
        DateTime since,
        DateTime now,
        CancellationToken ct)
    {
        // Last successful delivery.
        var lastDelivered = await notificationsCol
            .Find(Builders<BsonDocument>.Filter.Eq("telegram_delivery_status", "delivered"))
            .SortByDescending(n => n["last_telegram_attempt_at"])
            .FirstOrDefaultAsync(ct);

        // Daily breakdown for the lookback window.
        var pipeline = new BsonDocument[]
        {
            new()
            {
                ["$match"] = new BsonDocument
                {
                    ["telegram_delivery_status"] = new BsonDocument("$ne", BsonNull.Value),
                    ["generated_at"] = new BsonDocument
                    {
                        ["$gte"] = since,
                        ["$lte"] = now
                    }
                }
            },
            new()
            {
                ["$group"] = new BsonDocument
                {
                    ["_id"] = new BsonDocument
                    {
                        ["$dateToString"] = new BsonDocument
                        {
                            ["format"] = "%Y-%m-%d",
                            ["date"] = "$generated_at"
                        }
                    },
                    ["attempts"] = new BsonDocument("$sum", 1),
                    ["delivered"] = new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$telegram_delivery_status", "delivered" }), 1, 0
                    })),
                    ["failed"] = new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray
                    {
                        new BsonDocument("$eq", new BsonArray { "$telegram_delivery_status", "failed" }), 1, 0
                    }))
                }
            },
            new() { ["$sort"] = new BsonDocument("_id", 1) }
        };

        var results = await notificationsCol
            .Aggregate<BsonDocument>(pipeline, cancellationToken: ct)
            .ToListAsync(ct);

        var breakdown = results.Select(r => (object)new
        {
            date = r["_id"].AsString,
            attempts = r.GetValue("attempts", 0).AsInt32,
            delivered = r.GetValue("delivered", 0).AsInt32,
            failed = r.GetValue("failed", 0).AsInt32
        }).ToList();

        return new
        {
            last_successful_delivery_at = lastDelivered?.GetValue("last_telegram_attempt_at", BsonNull.Value)
                ?.ToNullableUniversalTime(),
            daily_breakdown = breakdown
        };
    }

    // ── Kill switch ─────────────────────────────────────────────────────────

    private static async Task<object> BuildKillSwitchAsync(
        IMongoCollection<BsonDocument> sysConfigCol,
        IMongoCollection<BsonDocument> auditCol,
        CancellationToken ct)
    {
        var ksDoc = await sysConfigCol
            .Find(Builders<BsonDocument>.Filter.Eq("key", KillSwitchKey))
            .FirstOrDefaultAsync(ct);
        var isActive = ksDoc is not null
            && ksDoc.Contains("value")
            && ksDoc["value"] is BsonBoolean b
            && b.Value;

        // Find the last activation/deactivation event.
        var lastChange = await auditCol
            .Find(Builders<BsonDocument>.Filter.In("action_type",
                new BsonArray { "kill_switch_activated", "kill_switch_deactivated" }))
            .SortByDescending(e => e["event_at"])
            .FirstOrDefaultAsync(ct);

        return new
        {
            active = isActive,
            last_change_at = lastChange?.GetValue("event_at", BsonNull.Value)?.ToNullableUniversalTime(),
            last_change_actor = lastChange?.GetValue("actor_id", "")?.AsString
        };
    }

    // ── Market halt ─────────────────────────────────────────────────────────

    private static async Task<object> BuildMarketHaltAsync(
        IMongoCollection<BsonDocument> auditCol,
        DateTime since,
        DateTime now,
        CancellationToken ct)
    {
        // Find the most recent halt-related audit events.
        var haltEvents = await auditCol
            .Find(Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.In("action_type",
                    new BsonArray { "market_halt_detected", "market_halt_cleared", "market_halt_admin_override" }),
                Builders<BsonDocument>.Filter.Gte("event_at", since)))
            .SortByDescending(e => e["event_at"])
            .ToListAsync(ct);

        var latest = haltEvents.FirstOrDefault();
        var isHalted = latest?.GetValue("action_type", "")?.AsString == "market_halt_detected"
                     || latest?.GetValue("action_type", "")?.AsString == "market_halt_admin_override";

        // Determine if the current state is auto-detected or admin-overridden.
        var source = latest?.GetValue("action_type", "")?.AsString == "market_halt_admin_override"
            ? "admin_override"
            : "auto_detected";

        var history = haltEvents.Select(e => (object)new
        {
            event_at = e.GetValue("event_at", BsonNull.Value).ToNullableUniversalTime(),
            action_type = e.GetValue("action_type", "")?.AsString ?? "",
            details = e.GetValue("details", BsonNull.Value).IsBsonNull ? null : e["details"]
        }).ToList();

        // Find halt start time.
        var haltStart = isHalted
            ? haltEvents
                .Where(e => e.GetValue("action_type", "")?.AsString == "market_halt_detected")
                .Select(e => e.GetValue("event_at", BsonNull.Value)?.ToNullableUniversalTime())
                .FirstOrDefault()
            : null;

        return new
        {
            status = isHalted ? "halted" : "clear",
            source,
            started_at = haltStart,
            history
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string ComputeJobStatus(
        BsonDocument? lastRun,
        List<object> dailyBreakdown)
    {
        if (lastRun is null)
            return "critical";

        var outcome = lastRun.GetValue("outcome", "")?.AsString ?? "";
        if (outcome == "failed")
            return "critical";

        // Check if the last `lookback_days` have any errors.
        var hasRecentErrors = dailyBreakdown
            .Cast<dynamic>()
            .Any(d => d.errors > 0);
        if (hasRecentErrors)
            return "warning";

        if (outcome == "skipped" || outcome == "partial")
            return "warning";

        return "healthy";
    }

    private static string JobTypeLabel(string jobType) => jobType switch
    {
        "DS" => "DataSync",
        "HDS" => "HistoricDataSeed",
        "EODSR" => "EOD Signal Runner",
        "LMDS" => "Live Market Data Scan",
        "LADS" => "Live Account Data Scan",
        "NDJ" => "Notification Delivery Job",
        "SYMBOL_PROBE" => "Symbol Validity Probe",
        _ => jobType
    };

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

public sealed record SymbolProbeSummaryResponse(
    DateTime? LastRunAt,
    int TotalSymbols,
    int FlaggedCount,
    List<string> FlaggedSymbols,
    string? Outcome,
    bool ShowBanner);
