using System.Security.Claims;
using System.Text.Json;
using MongoDB.Bson;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.Auth;
using SignalStack.Api.Sessions;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using SignalStack.Notifications.TelegramBot;

namespace SignalStack.Api.TelegramBot;

/// <summary>
/// Admin API endpoints for Telegram bot provisioning and management.
///
/// REQ-NOTIFY-015: provision, view, replace bot token; test-send.
/// REQ-NOTIFY-022a: token rotation, global-disable integration.
/// REQ-SEC-011: token replacement requires step-up re-authentication.
/// </summary>
public static class TelegramBotEndpoints
{
    private static readonly TimeSpan StepUpDuration = TimeSpan.FromMinutes(5);

    public static IEndpointRouteBuilder MapTelegramBotEndpoints(
        this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/telegram-bot");

        // GET /api/v1/admin/telegram-bot — current bot status
        // REQ-NOTIFY-015: admin portal displays bot username, last delivery timestamp,
        // token age, and health indicator.
        admin.MapGet("/", async (
            HttpContext context,
            TelegramBotService botService,
            IUserRepository userRepo) =>
        {
            var adminId = GetUserId(context);
            var user = await userRepo.FindByUserIdAsync(adminId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var status = await botService.GetActiveBotStatusAsync(context.RequestAborted);
            if (status is null)
                return Results.Ok(new { bot_provisioned = false });

            return Results.Ok(new
            {
                bot_provisioned = true,
                status.BotId,
                status.BotUsername,
                status.IsActive,
                status.HasTokenConfigured,
                status.TokenAgeDays,
                status.RotationDueDays,
                status.RotationOverdue,
                status.LastTokenReplacedAt,
                status.LastSuccessfulDeliveryAt,
                status.LastDeliveryCheckAt,
                status.ProvisionedAt,
            });
        }).RequireAuthorization();

        // PUT /api/v1/admin/telegram-bot/token — replace bot token
        // REQ-NOTIFY-015: validate with Telegram getMe before writing;
        // replacement takes effect immediately.
        // REQ-SEC-011: requires step-up re-authentication.
        // REQ-NOTIFY-022a(c): disable-and-re-enable cycle.
        admin.MapPut("/token", async (
            HttpContext context,
            TelegramBotService botService,
            IUserRepository userRepo,
            ISessionRepository sessionRepo,
            IAuditEventRepository auditRepo) =>
        {
            var adminId = GetUserId(context);
            var user = await userRepo.FindByUserIdAsync(adminId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var body = await context.Request.ReadFromJsonAsync<ReplaceTokenRequest>(
                cancellationToken: context.RequestAborted);
            if (body is null || string.IsNullOrWhiteSpace(body.Token))
                return Results.BadRequest(new { error = "token_required" });

            // REQ-SEC-011: step-up re-authentication required for token replacement.
            var jti = context.User.FindFirst("jti")?.Value ?? "";
            var session = await sessionRepo.FindBySessionTokenAsync(jti, context.RequestAborted);
            var stepUpValid = session?.StepUpAuthenticatedAt.HasValue == true
                && session.StepUpAuthenticatedAt.Value >= DateTime.UtcNow - StepUpDuration;

            if (!stepUpValid)
            {
                return Results.Json(
                    new { error = "step_up_required", action = "replace_telegram_bot_token" },
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var result = await botService.ProvisionBotAsync(
                body.Token, adminId, context.RequestAborted);

            if (!result.Success)
            {
                return Results.BadRequest(new
                {
                    error = result.ErrorCode,
                    detail = result.ErrorDetail
                });
            }

            return Results.Ok(new
            {
                success = true,
                result.BotId,
                result.BotUsername
            });
        }).RequireAuthorization();

        // POST /api/v1/admin/telegram-bot/test-send — send test message
        // REQ-NOTIFY-015: test-send to admin's own linked Telegram account.
        // Fails visibly if admin has no linked account or delivery fails.
        admin.MapPost("/test-send", async (
            HttpContext context,
            TelegramBotService botService,
            IUserRepository userRepo) =>
        {
            var adminId = GetUserId(context);
            var user = await userRepo.FindByUserIdAsync(adminId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var result = await botService.TestSendAsync(adminId, context.RequestAborted);

            if (!result.Success)
            {
                return Results.BadRequest(new
                {
                    error = "test_send_failed",
                    detail = result.ErrorDetail
                });
            }

            return Results.Ok(new { success = true });
        }).RequireAuthorization();

        // GET /api/v1/admin/telegram-bot/rotation — token rotation info
        // REQ-NOTIFY-022a: admin can check rotation status.
        admin.MapGet("/rotation", async (
            HttpContext context,
            TelegramBotService botService,
            IUserRepository userRepo) =>
        {
            var adminId = GetUserId(context);
            var user = await userRepo.FindByUserIdAsync(adminId, context.RequestAborted);
            if (user is null || user.Role != UserRole.Admin)
                return Results.Forbid();

            var info = await botService.GetTokenRotationInfoAsync(context.RequestAborted);
            return Results.Ok(info);
        }).RequireAuthorization();

        return app;
    }

    private static string GetUserId(HttpContext context)
    {
        return context.User.FindFirst(JwtRegisteredClaimNamesCompat.Sub)?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? "";
    }
}

public sealed record ReplaceTokenRequest(string Token);
