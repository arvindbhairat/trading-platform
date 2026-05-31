using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Execution;
using SignalStack.Api.Observability;
using SignalStack.Domain.Execution;
using SignalStack.Storage.Execution;

namespace Microsoft.AspNetCore.Routing;

/// <summary>
/// Execution assistance API endpoints (Phase 7).
/// Wires pre-flight checks, order context, and signed-payload endpoint.
/// P7-T3: Pre-flight check endpoint.
/// P7-T4: Order context endpoint.
/// P7-T5: Signed-payload endpoint.
/// </summary>
public static class ExecutionEndpoints
{
    public static IEndpointRouteBuilder MapExecutionEndpoints(this IEndpointRouteBuilder app)
    {
        var execution = app.MapGroup("/api/v1/execution").RequireAuthorization();

        // ── GET /api/v1/execution/pre-flight?symbol={symbol} ──────────────
        // Returns a structured set of checks the user must pass before the
        // FYERS execution widget is presented. P7-T3.
        execution.MapGet("/pre-flight", async (
            HttpContext context,
            PreFlightCheckService preFlightService,
            string? symbol) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var result = await preFlightService.CheckAsync(
                userId, symbol, context.RequestAborted);

            return Results.Ok(new PreFlightResponse(
                result.CanExecute,
                result.Checks,
                result.Checks.Count(c => !c.Passed && c.Severity == "blocking"),
                result.Checks.Count(c => !c.Passed && c.Severity == "warning")
            ));
        });

        // ── GET /api/v1/execution/order-context?symbol={symbol}&action={action} ─
        // Returns RME sizing, portfolio heat, stop levels, and market state
        // for the Phase 1 platform modal. P7-T4 / REQ-ORDER-007/008.
        execution.MapGet("/order-context", async (
            HttpContext context,
            OrderContextService orderContextService,
            string symbol,
            string action) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(symbol))
                return Results.BadRequest(new ApiErrorResponse("symbol query parameter is required."));

            var validActions = new[] { "entry", "add", "reduce", "exit" };
            if (!validActions.Contains(action.ToLowerInvariant()))
                return Results.BadRequest(new ApiErrorResponse($"action must be one of: {string.Join(", ", validActions)}"));

            var result = await orderContextService.GetOrderContextAsync(
                userId, symbol, action, context.RequestAborted);

            return Results.Ok(result);
        });

        // ── POST /api/v1/execution/intent/signed-payload ────────────────────
        // Returns an HMAC-signed payload for the <fyers-button> element.
        // Applies the multi-intent heat gate (EC-5) and LADS health gate (RME-R2)
        // before signing. P7-T5 / REQ-ORDER-009b/009c.
        execution.MapPost("/intent/signed-payload", async (
            HttpContext context,
            IIntentSigningService signingService,
            SignedPayloadRequest request) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            var sessionId = context.User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value ?? "";

            try
            {
                var result = await signingService.CreateSignedPayloadAsync(
                    userId, sessionId, request, context.RequestAborted);

                return Results.Ok(result);
            }
            catch (SignedPayloadSigningException ex)
            {
                return Results.UnprocessableEntity(new SignedPayloadErrorResponse(ex.Error));
            }
        });

        // ── POST /api/v1/execution/intent/callback ────────────────────────────
        // Called by the frontend after receiving the FYERS widget finished callback.
        // Updates the intent_ledger record to matched or submission_failed.
        // P7-T7 / REQ-ORDER-015/015e/015f.
        execution.MapPost("/intent/callback", async (
            HttpContext context,
            IIntentLedgerRepository intentLedger,
            IntentCallbackRequest request) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Nonce))
                return Results.BadRequest(new ApiErrorResponse("nonce is required."));

            var validStatuses = new[] { IntentStatus.Matched, IntentStatus.SubmissionFailed };
            if (!validStatuses.Contains(request.Status))
                return Results.BadRequest(new ApiErrorResponse($"status must be one of: {string.Join(", ", validStatuses)}."));

            var updated = await intentLedger.UpdateIntentFromCallbackAsync(
                request.Nonce, request.Status, request.RequestToken, context.RequestAborted);

            if (updated is null)
                return Results.NotFound(new ApiErrorResponse("Intent not found for the given nonce or already updated."));

            // ── Emit callback reliability metric (REQ-ORDER-015d) ─────────
            // Tagged by status (matched / submission_failed) and user class.
            // All Phase A users are production; tag infrastructure supports
            // test vs. production when the distinction is added later.
            ApiTelemetry.OrderCallbackReceivedTotal.Add(1,
                new KeyValuePair<string, object?>("status", updated.Status),
                new KeyValuePair<string, object?>("action_type", updated.Action),
                new KeyValuePair<string, object?>("user_class", "production"));

            return Results.Ok(new IntentCallbackResponse(
                updated.Nonce,
                updated.Status,
                updated.MatchSource,
                updated.CallbackReceivedAt
            ));
        });

        // ── GET /api/v1/execution/intent/pending-confirmations?symbol={symbol} ─
        // Returns intents in "matched" status for pending fill confirmation
        // state (REQ-ORDER-015e). Optionally filtered by symbol. Includes
        // submission timestamp and the user's last LADS sync timestamp.
        execution.MapGet("/intent/pending-confirmations", async (
            HttpContext context,
            IIntentLedgerRepository intentLedger,
            IMongoDatabase database,
            string? symbol) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            // Get matched intents awaiting position advancement
            var matchedIntents = await intentLedger.GetAwaitingConfirmationByUserAsync(
                userId, symbol, context.RequestAborted);

            if (matchedIntents.Count == 0)
                return Results.Ok(new PendingConfirmationsResponse(Array.Empty<PendingConfirmationItem>()));

            // Get the user's last successful LADS sync timestamp
            var accountSync = database.GetCollection<BsonDocument>("fyers_account_sync");
            var syncFilter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
            var syncDoc = await accountSync
                .Find(syncFilter)
                .Project<BsonDocument>(Builders<BsonDocument>.Projection
                    .Include("last_successful_sync_at"))
                .FirstOrDefaultAsync(context.RequestAborted);

            DateTime? lastSyncAt = null;
            if (syncDoc?.TryGetValue("last_successful_sync_at", out var syncVal) == true
                && !syncVal.IsBsonNull)
            {
                lastSyncAt = syncVal.ToUniversalTime();
            }

            var result = matchedIntents.Select(intent => new PendingConfirmationItem(
                intent.Symbol,
                intent.Action,
                intent.Side,
                intent.Quantity,
                intent.OrderType,
                intent.CallbackReceivedAt ?? intent.CreatedAt,
                intent.CallbackReceivedAt,
                intent.CreatedAt,
                lastSyncAt,
                intent.Nonce
            ));

            return Results.Ok(new PendingConfirmationsResponse(result));
        });

        return app;
    }
}

public sealed record PreFlightResponse(
    bool CanExecute,
    IReadOnlyList<CheckResult> Checks,
    int BlockingCount,
    int WarningCount
);

public sealed record IntentCallbackResponse(
    string Nonce,
    string Status,
    string? MatchSource,
    DateTime? CallbackReceivedAt
);

public sealed record PendingConfirmationItem(
    string Symbol,
    string Action,
    string Side,
    int Quantity,
    string OrderType,
    DateTime SubmissionTimestamp,
    DateTime? CallbackReceivedAt,
    DateTime IntentCreatedAt,
    DateTime? LastLadsSyncAt,
    string Nonce
);

public sealed record PendingConfirmationsResponse(
    IEnumerable<PendingConfirmationItem> PendingConfirmations
);

public sealed record ApiErrorResponse(string Error);

public sealed record SignedPayloadErrorResponse(SignedPayloadError Error);
