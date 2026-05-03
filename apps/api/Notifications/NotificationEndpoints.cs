using System.Security.Claims;
using MongoDB.Bson;

namespace SignalStack.Api.Notifications;

/// <summary>
/// Minimal API endpoints for the portal notification feed.
/// P5-T6 / REQ-NOTIFY-014, REQ-PROFILE-006.
/// </summary>
public static class NotificationEndpoints
{
    private const string Tag = "Notifications";

    /// <summary>
    /// Human-readable labels for user-facing notification types.
    /// Used by the portal feed to render type names.
    /// </summary>
    private static readonly Dictionary<string, string> TypeLabels = new(StringComparer.Ordinal)
    {
        [NotificationType.EntrySignal] = "Entry Signal",
        [NotificationType.ExitAlert] = "Exit Alert",
        [NotificationType.AddAdvisory] = "Add Advisory",
        [NotificationType.ReduceAdvisory] = "Reduce Advisory",
        [NotificationType.PortfolioImpactInsight] = "Portfolio Impact",
        [NotificationType.DrawdownModeAlert] = "Drawdown Mode Alert",
        [NotificationType.PortfolioHeatWarning] = "Portfolio Heat Warning",
        [NotificationType.GapRiskAlert] = "Gap Risk Alert",
        [NotificationType.CircuitLimitAlert] = "Circuit Limit Alert",
        [NotificationType.CorporateActionWarning] = "Corporate Action Warning",
        [NotificationType.PendingEntrySuperseded] = "Pending Entry Superseded",
        [NotificationType.PendingEntryExpired] = "Pending Entry Expired",
        [NotificationType.SectorExposureBreach] = "Sector Exposure Breach",
        [NotificationType.MarketHaltActive] = "Market Halt Active",
        [NotificationType.MarketHaltCleared] = "Market Halt Cleared",
        [NotificationType.EquityBaseDivergenceAdvisory] = "Equity Base Divergence",
    };

    public static IEndpointRouteBuilder MapNotificationEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications");

        // ── GET /api/v1/notifications ──────────────────────────────────────
        // User notification feed with optional filtering and pagination.
        // REQ-NOTIFY-012: filter by type, symbol, date range, delivery status.
        group.MapGet("/", async (
            HttpContext context,
            INotificationRepository repo,
            int limit = 50,
            int skip = 0,
            string? type = null,
            string? symbol = null,
            DateTime? dateFrom = null,
            DateTime? dateTo = null,
            string? deliveryStatus = null) =>
        {
            var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userIdStr) || !ObjectId.TryParse(userIdStr, out var userId))
                return Results.Unauthorized();

            var list = await repo.GetByUserIdFilteredAsync(
                userId, limit, skip, type, symbol, dateFrom, dateTo, deliveryStatus,
                context.RequestAborted);

            return Results.Ok(list.Select(n => MapToFeedDto(n)));
        }).RequireAuthorization().WithTags(Tag);

        // ── GET /api/v1/notifications/unread-count ─────────────────────────
        // Returns critical and total unread counts.
        // REQ-NOTIFY-014: two-count indicator (critical · total).
        group.MapGet("/unread-count", async (
            HttpContext context,
            INotificationRepository repo) =>
        {
            var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userIdStr) || !ObjectId.TryParse(userIdStr, out var userId))
                return Results.Unauthorized();

            var totalUnread = await repo.CountUnreadByUserAsync(userId, context.RequestAborted);
            var criticalUnread = await repo.CountUnreadCriticalByUserAsync(userId, context.RequestAborted);

            return Results.Ok(new
            {
                total_unread = totalUnread,
                critical_unread = criticalUnread,
            });
        }).RequireAuthorization().WithTags(Tag);

        // ── POST /api/v1/notifications/{id}/read ───────────────────────────
        // Mark a single notification as read.
        group.MapPost("/{id}/read", async (
            string id,
            HttpContext context,
            INotificationRepository repo) =>
        {
            var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userIdStr) || !ObjectId.TryParse(userIdStr, out var _))
                return Results.Unauthorized();

            if (!ObjectId.TryParse(id, out var oid))
                return Results.BadRequest(new { error = "Invalid notification ID." });

            await repo.MarkAsReadAsync(oid, context.RequestAborted);
            return Results.Ok(new { status = "read" });
        }).RequireAuthorization().WithTags(Tag);

        // ── POST /api/v1/notifications/mark-all-read ───────────────────────
        // Mark all unread notifications for the current user as read.
        group.MapPost("/mark-all-read", async (
            HttpContext context,
            INotificationRepository repo) =>
        {
            var userIdStr = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userIdStr) || !ObjectId.TryParse(userIdStr, out var userId))
                return Results.Unauthorized();

            var count = await repo.MarkAllAsReadAsync(userId, context.RequestAborted);
            return Results.Ok(new { status = "all_read", count });
        }).RequireAuthorization().WithTags(Tag);

        // ── GET /api/v1/notifications/types ────────────────────────────────
        // List available user-facing notification types with labels and critical flag.
        // Used by the portal filter UI.
        group.MapGet("/types", () =>
        {
            var types = NotificationType.UserFacing.Select(t => new
            {
                type = t,
                label = TypeLabels.GetValueOrDefault(t, t),
                is_critical = NotificationType.Critical.Contains(t),
            });

            return Results.Ok(types);
        }).RequireAuthorization().WithTags(Tag);

        return app;
    }

    // ── DTO mapping ───────────────────────────────────────────────────────

    private static object MapToFeedDto(NotificationDocument n)
    {
        var isFailed = string.Equals(
            n.TelegramDeliveryStatus, DeliveryStatus.Failed, StringComparison.Ordinal);

        return new
        {
            id = n.Id.ToString(),
            notification_type = n.NotificationType,
            type_label = TypeLabels.GetValueOrDefault(n.NotificationType, n.NotificationType),
            is_critical = NotificationType.Critical.Contains(n.NotificationType),
            signal_type_name = n.SignalTypeName,
            timeframe = n.Timeframe,
            symbol = n.Symbol,
            content = n.Content,
            deep_link = n.DeepLink,
            generated_at = n.GeneratedAt.ToString("o"),
            is_read = n.IsRead,
            telegram_delivery_status = n.TelegramDeliveryStatus,
            telegram_delivery_attempts = n.TelegramDeliveryAttempts,
            delivery_failed = isFailed,
            error_details = isFailed ? n.ErrorDetails : null,
        };
    }
}
