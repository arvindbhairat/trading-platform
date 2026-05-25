using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Domain.Users;

namespace SignalStack.Api.Users;

/// <summary>
/// User profile settings API endpoints, including the equity-base override
/// with S-10 cap enforcement (REQ-RME-006c).
/// </summary>
public static class UserProfileExtensions
{
    public static IEndpointRouteBuilder MapUserProfileEndpoints(this IEndpointRouteBuilder app)
    {
        var profile = app.MapGroup("/api/v1/user/profile").RequireAuthorization();

        // ── GET /api/v1/user/profile/equity ─────────────────────────────────
        // Returns the user's current equity base state including override status,
        // FYERS total, platform-visible equity, divergence advisory info, and
        // the max permitted override.
        // REQ-RME-006b/c/e.
        profile.MapGet("/equity", async (
            HttpContext context,
            IMongoDatabase database) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var users = database.GetCollection<UserDocument>("users");
            var user = await users
                .Find(u => u.UserId == userId)
                .FirstOrDefaultAsync(context.RequestAborted);

            if (user is null)
                return Results.NotFound(new { error = "User not found." });

            // Read FYERS total from portfolio_snapshots
            var snapshots = database.GetCollection<BsonDocument>("portfolio_snapshots");
            var snapshot = await snapshots
                .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId))
                .FirstOrDefaultAsync(context.RequestAborted);

            decimal? fyersTotal = null;
            decimal? cashReserve = null;
            if (snapshot is not null)
            {
                var mv = snapshot.GetValue("current_market_value", BsonNull.Value);
                var cash = snapshot.GetValue("cash_reserve", BsonNull.Value);

                var mvVal = mv.IsBsonNull || !mv.IsDecimal128 ? 0m : (decimal)mv.AsDecimal128;
                var cashVal = cash.IsBsonNull || !cash.IsDecimal128 ? 0m : (decimal)cash.AsDecimal128;

                fyersTotal = mvVal + cashVal;
                cashReserve = cashVal;
            }

            // Read platform-visible equity from equity_curve (most authoritative).
            decimal? platformVisible = null;
            decimal? externalHoldings = null;
            var equityCurve = database.GetCollection<BsonDocument>("equity_curve");
            var curveFilter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
            var sort = Builders<BsonDocument>.Sort.Descending("session_date");
            var latestCurve = await equityCurve
                .Find(curveFilter)
                .Sort(sort)
                .Limit(1)
                .FirstOrDefaultAsync(context.RequestAborted);

            if (latestCurve is not null)
            {
                var eqValue = latestCurve.GetValue("equity_value", BsonNull.Value);
                if (!eqValue.IsBsonNull && eqValue.IsDecimal128)
                {
                    platformVisible = (decimal)eqValue.AsDecimal128;
                }
            }

            // Fall back to portfolio_snapshots if equity_curve unavailable.
            if (platformVisible is null && fyersTotal.HasValue)
            {
                platformVisible = fyersTotal;
            }

            // Compute external holdings = FYERS total − platform-visible equity.
            if (fyersTotal.HasValue && platformVisible.HasValue)
            {
                externalHoldings = fyersTotal.Value - platformVisible.Value;
            }

            // S-10 cap: min(FYERS_total × 1.5, FYERS_total + ₹10,00,000)
            decimal? maxOverride = null;
            if (fyersTotal.HasValue)
            {
                maxOverride = Math.Min(
                    fyersTotal.Value * 1.5m,
                    fyersTotal.Value + 1_000_000m);
            }

            // ── Divergence assessment (REQ-RME-006e) ──────────────────────────
            bool hasDivergence = false;
            decimal? divergenceGapPct = null;
            decimal divergenceThresholdPct = 15m; // Default unless sys_config says otherwise

            // Read threshold from sys_config
            var sysConfig = database.GetCollection<BsonDocument>("sys_config");
            var thresholdDoc = await sysConfig
                .Find(Builders<BsonDocument>.Filter.Eq("key",
                    "risk.equity_base.external_divergence_warn_pct"))
                .FirstOrDefaultAsync(context.RequestAborted);

            if (thresholdDoc is not null)
            {
                var tv = thresholdDoc.GetValue("value", BsonNull.Value);
                if (!tv.IsBsonNull && decimal.TryParse(tv.AsString,
                        System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var parsedThreshold))
                {
                    divergenceThresholdPct = parsedThreshold;
                }
            }

            if (fyersTotal.HasValue && platformVisible.HasValue && platformVisible.Value > 0)
            {
                var gap = Math.Abs(fyersTotal.Value - platformVisible.Value);
                var gapPct = Math.Round(gap / platformVisible.Value * 100m, 2);
                divergenceGapPct = gapPct;
                hasDivergence = gapPct > divergenceThresholdPct;
            }

            return Results.Ok(new
            {
                equity_base_override = user.EquityBaseOverride,
                equity_base_override_updated_at = user.EquityBaseOverrideUpdatedAt,
                fyers_total_account_value = fyersTotal,
                platform_visible_equity = platformVisible,
                external_holdings_excluded = externalHoldings,
                platform_visible_override_max = maxOverride,
                override_is_active = user.EquityBaseOverride.HasValue,
                has_divergence = hasDivergence,
                divergence_gap_pct = divergenceGapPct,
                external_divergence_warn_pct = divergenceThresholdPct,
            });
        });

        // ── PUT /api/v1/user/profile/equity/override ────────────────────────
        // Sets the manual equity base override with S-10 cap enforcement.
        // REQ-RME-006c: cap = min(FYERS_total × 1.5, FYERS_total + ₹10,00,000).
        profile.MapPut("/equity/override", async (
            SetOverrideRequest request,
            HttpContext context,
            IMongoDatabase database,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (request.OverrideValue <= 0)
                return Results.BadRequest(new
                {
                    error = "Override value must be greater than zero."
                });

            // Read FYERS total from portfolio_snapshots for cap calculation
            var snapshots = database.GetCollection<BsonDocument>("portfolio_snapshots");
            var snapshot = await snapshots
                .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId))
                .FirstOrDefaultAsync(context.RequestAborted);

            decimal fyersTotal = 0;
            if (snapshot is not null)
            {
                var mv = snapshot.GetValue("current_market_value", BsonNull.Value);
                var cash = snapshot.GetValue("cash_reserve", BsonNull.Value);

                var mvVal = mv.IsBsonNull || !mv.IsDecimal128 ? 0m : (decimal)mv.AsDecimal128;
                var cashVal = cash.IsBsonNull || !cash.IsDecimal128 ? 0m : (decimal)cash.AsDecimal128;

                fyersTotal = mvVal + cashVal;
            }

            if (fyersTotal <= 0)
            {
                return Results.BadRequest(new
                {
                    error = "Cannot set an equity override until a portfolio snapshot " +
                            "is available. Please wait for your first account sync to complete."
                });
            }

            // S-10 cap: min(FYERS_total × 1.5, FYERS_total + ₹10,00,000)
            var maxAllowed = Math.Min(
                fyersTotal * 1.5m,
                fyersTotal + 1_000_000m);

            if (request.OverrideValue > maxAllowed)
            {
                return Results.BadRequest(new
                {
                    error = $"Override value ₹{request.OverrideValue:N0} exceeds the maximum " +
                            $"permitted value of ₹{maxAllowed:N0}. " +
                            $"Your current FYERS total account value is ₹{fyersTotal:N0}. " +
                            $"The cap is min(FYERS_total × 1.5, FYERS_total + ₹10,00,000)."
                });
            }

            await userRepo.SetEquityOverrideAsync(userId, request.OverrideValue, context.RequestAborted);

            // Audit trail
            await auditRepo.RecordAsync(
                userId,
                "equity_override_set",
                DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["override_value"] = request.OverrideValue,
                    ["fyers_total"] = fyersTotal,
                    ["max_allowed"] = maxAllowed,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new
            {
                message = $"Equity override set to ₹{request.OverrideValue:N0}.",
                equity_base_override = request.OverrideValue,
                equity_base_override_max = maxAllowed,
            });
        });

        // ── DELETE /api/v1/user/profile/equity/override ─────────────────────
        // Clears the manual equity base override, reverting to auto-derived equity.
        // REQ-RME-006c.
        profile.MapDelete("/equity/override", async (
            HttpContext context,
            IUserRepository userRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            await userRepo.SetEquityOverrideAsync(userId, null, context.RequestAborted);

            // Audit trail
            await auditRepo.RecordAsync(
                userId,
                "equity_override_cleared",
                DateTime.UtcNow,
                cancellationToken: context.RequestAborted);

            return Results.Ok(new { message = "Equity override cleared. Using auto-derived equity base." });
        });

        return app;
    }
}

/// <summary>
/// Request body for PUT /api/v1/user/profile/equity/override.
/// </summary>
public sealed record SetOverrideRequest
{
    public decimal OverrideValue { get; init; }
}
