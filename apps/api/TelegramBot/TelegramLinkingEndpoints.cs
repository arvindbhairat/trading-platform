using System.Security.Claims;
using MongoDB.Bson;
using SignalStack.Api.Auth;
using SignalStack.Api.Users;
using SignalStack.Domain.Users;
using SignalStack.Notifications.TelegramBot;

namespace SignalStack.Api.TelegramBot;

/// <summary>
/// API endpoints for user Telegram linking flow.
///
/// REQ-NOTIFY-016: generate linking token, validate and link.
/// REQ-NOTIFY-017: status endpoint for dashboard prompt.
/// REQ-NOTIFY-018: unlink Telegram account.
/// </summary>
public static class TelegramLinkingEndpoints
{
    public static IEndpointRouteBuilder MapTelegramLinkingEndpoints(
        this IEndpointRouteBuilder app)
    {
        var telegram = app.MapGroup("/api/v1/telegram");

        // POST /api/v1/telegram/linking-token — generate linking token
        // REQ-NOTIFY-016: single-use time-limited token for linking flow.
        telegram.MapPost("/linking-token", async (
            HttpContext context,
            TelegramLinkingService linkingService,
            IUserRepository userRepo) =>
        {
            var userId = GetUserId(context);
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Forbid();

            // REQ-NOTIFY-017: if already linked, allow re-link (generates new token).
            var result = await linkingService.GenerateTokenAsync(
                user.Id, context.RequestAborted);

            return Results.Ok(result);
        }).RequireAuthorization();

        // POST /api/v1/telegram/link — validate token and link chat ID
        // REQ-NOTIFY-016: bot calls this endpoint when user sends the token.
        // Accepts: { token: "hex", chat_id: 123456789 }
        // This endpoint is called by the bot webhook, not directly by the user.
        // In production, the bot server pushes updates here.
        // The endpoint is intentionally unauthenticated (the token itself is the auth).
        telegram.MapPost("/link", async (
            HttpContext context,
            TelegramLinkingService linkingService) =>
        {
            var body = await context.Request.ReadFromJsonAsync<LinkChatRequest>(
                cancellationToken: context.RequestAborted);

            if (body is null || string.IsNullOrWhiteSpace(body.Token))
                return Results.BadRequest(new ErrorResponse("token_required"));

            if (body.ChatId <= 0)
                return Results.BadRequest(new ErrorResponse("invalid_chat_id"));

            var result = await linkingService.ValidateAndLinkAsync(
                body.Token, body.ChatId, context.RequestAborted);

            if (!result.Success)
            {
                return Results.BadRequest(new LinkErrorResponse(
                    result.ErrorCode,
                    result.ErrorDetail));
            }

            return Results.Ok(new SuccessResponse(true));
        });

        // POST /api/v1/telegram/unlink — unlink Telegram account
        // REQ-NOTIFY-018: stops further Telegram delivery; existing records retained.
        telegram.MapPost("/unlink", async (
            HttpContext context,
            TelegramLinkingService linkingService,
            IUserRepository userRepo) =>
        {
            var userId = GetUserId(context);
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Forbid();

            var result = await linkingService.UnlinkAsync(
                user.Id, context.RequestAborted);

            if (!result.Success)
                return Results.BadRequest(new ErrorResponse(result.ErrorDetail!));

            return Results.Ok(new SuccessResponse(true));
        }).RequireAuthorization();

        // GET /api/v1/telegram/status — link status
        // REQ-NOTIFY-017: used by portal to show link status and prompt.
        telegram.MapGet("/status", async (
            HttpContext context,
            TelegramLinkingService linkingService,
            IUserRepository userRepo) =>
        {
            var userId = GetUserId(context);
            var user = await userRepo.FindByUserIdAsync(userId, context.RequestAborted);
            if (user is null)
                return Results.Forbid();

            var status = await linkingService.GetLinkStatusAsync(
                user.Id, context.RequestAborted);

            return Results.Ok(status);
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

public sealed record LinkChatRequest(string Token, long ChatId);

// ── Response records ───────────────────────────────────────────────────────

public sealed record LinkErrorResponse(string? Error, string? Detail);
