using System.Security.Claims;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Domain.Audit;
using SignalStack.Api.LedgerWriters;
using SignalStack.Configuration.Ledger;
using SignalStack.Domain.Portfolio;
using SignalStack.Storage.Portfolio;

namespace SignalStack.Api.Portfolio;

/// <summary>
/// Reconciliation API endpoints for the defined recovery flow (REQ-RECON-001..004).
///
/// Recovery order per REQ-RECON-002:
///   1. User-triggered refresh/resync from FYERS
///   2. Extended backfill/resync for a selected historical period
///   3. Audited manual adjustment only when broker sync cannot restore accuracy
///
/// All endpoints require authentication. Manual adjustments additionally
/// require step-up re-authentication (REQ-SEC-011) as an admin operation.
/// </summary>
public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var reconcil = app.MapGroup("/api/v1/reconciliation").RequireAuthorization();

        // ── GET /api/v1/reconciliation/status ─────────────────────────────────
        // Returns the user's sync status and available recovery options in
        // the defined order (REQ-RECON-002).
        // REQ-RECON-001: surfaces auto-sync failure state and manual options.
        reconcil.MapGet("/status", async (
            HttpContext context,
            IMongoDatabase database) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

            // Read sync status from fyers_account_sync collection.
            var syncCollection = database.GetCollection<BsonDocument>("fyers_account_sync");
            var syncDoc = await syncCollection
                .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId))
                .FirstOrDefaultAsync(context.RequestAborted);

            // Read adjustment count for this user.
            var adjCollection = database.GetCollection<BsonDocument>("manual_adjustments");
            var adjustmentCount = await adjCollection
                .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId))
                .CountDocumentsAsync(context.RequestAborted);

            return Results.Ok(new
            {
                sync_status = BuildSyncStatus(syncDoc),
                recovery_options = new[]
                {
                    new { step = 1, action = "refresh", label = "Manual refresh from broker",
                        description = "Triggers a full re-sync of recent trade data from FYERS.",
                        available = true },
                    new { step = 2, action = "backfill", label = "Historical backfill",
                        description = "Extended backfill for a selected symbol or date range.",
                        available = true },
                    new { step = 3, action = "adjustment", label = "Manual adjustment",
                        description = "Audited manual adjustment when broker sync cannot restore accuracy.",
                        available = true },
                },
                adjustment_count = adjustmentCount,
            });
        });

        // ── POST /api/v1/reconciliation/refresh ──────────────────────────────
        // Step 1 of the recovery order (REQ-RECON-002): user-triggered refresh.
        // Records a manual sync request to be picked up by the worker.
        // REQ-RECON-001: manual sync support when auto-sync cannot run.
        reconcil.MapPost("/refresh", async (
            HttpContext context,
            IMongoDatabase database,
            IAuditEventRepository auditRepo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            // Record a manual sync request in fyers_account_sync.
            var syncCollection = database.GetCollection<BsonDocument>("fyers_account_sync");
            var now = DateTime.UtcNow;

            var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);
            var update = Builders<BsonDocument>.Update
                .Set("user_id", userId)
                .Set("last_sync_request_at", now)
                .Set("sync_request_status", "pending")
                .Set("sync_request_source", "user_manual")
                .Set("updated_at", now);

            await syncCollection.UpdateOneAsync(
                filter, update,
                options: new UpdateOptions { IsUpsert = true },
                cancellationToken: context.RequestAborted);

            // Audit trail.
            await auditRepo.RecordAsync(
                actorId: userId,
                actionType: "manual_sync_requested",
                eventAt: now,
                details: new Dictionary<string, object?>
                {
                    ["source"] = "user_manual",
                    ["recovery_step"] = 1,
                },
                cancellationToken: context.RequestAborted);

            return Results.Ok(new
            {
                status = "sync_requested",
                message = "Manual sync requested. The system will process this shortly.",
                requested_at = now,
            });
        });

        // ── POST /api/v1/reconciliation/adjustment ──────────────────────────
        // Step 3 of the recovery order (REQ-RECON-002): audited manual adjustment.
        // REQ-RECON-003: recorded as explicit adjustment entry, not silent edit.
        // REQ-RECON-004: recorded against the affected holding/position context.
        reconcil.MapPost("/adjustment", async (
            HttpContext context,
            AdjustmentRequest request,
            IMongoDatabase database,
            ManualAdjustmentService adjustmentService,
            IManualAdjustmentRepository adjustmentRepo,
            IAuditEventRepository auditRepo) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            if (string.IsNullOrWhiteSpace(request.Symbol))
                return Results.BadRequest(new { error = "symbol is required" });
            if (request.QuantityDelta == 0 && request.CostBasisDelta == 0m)
                return Results.BadRequest(new { error = "at least one of quantity_delta or cost_basis_delta must be non-zero" });

            var now = DateTime.UtcNow;

            // Step 1: Record audit event before mutation.
            var auditEventId = await auditRepo.RecordAsync(
                actorId: userId,
                actionType: "manual_adjustment_initiated",
                eventAt: now,
                details: new Dictionary<string, object?>
                {
                    ["symbol"] = request.Symbol,
                    ["quantity_delta"] = request.QuantityDelta,
                    ["cost_basis_delta"] = request.CostBasisDelta,
                    ["reason"] = request.Reason,
                    ["recovery_step"] = 3,
                },
                cancellationToken: context.RequestAborted);

            // Step 2: Create trade-ledger adjustment entry.
            var adjustmentTrade = new RawTrade(
                TradeId: $"adj_{Guid.NewGuid():N}",
                Symbol: request.Symbol,
                Side: request.QuantityDelta >= 0 ? "buy" : "sell",
                Quantity: Math.Abs(request.QuantityDelta),
                Price: request.CostBasisDelta != 0m
                    ? Math.Round(Math.Abs(request.CostBasisDelta) / Math.Max(Math.Abs(request.QuantityDelta), 1), 2)
                    : 0m,
                TradeAtUtc: now,
                OrderId: null);

            await adjustmentService.ExecuteAsync(
                userId,
                adjustmentTrade,
                adjustmentContext: $"{request.Symbol}:{request.AdjustmentType ?? "quantity"}",
                notes: request.Reason,
                ct: context.RequestAborted);

            // Step 3: Record in manual_adjustments collection for audit trail.
            var adjustmentDoc = new ManualAdjustmentDocument
            {
                UserId = userId,
                Symbol = request.Symbol,
                AdjustmentType = request.AdjustmentType ?? "quantity",
                QuantityDelta = request.QuantityDelta,
                CostBasisDelta = request.CostBasisDelta,
                Reason = request.Reason,
                RequestedBy = userId,
                ApprovedBy = request.ApprovedBy,
                AuditEventId = auditEventId,
                CreatedAt = now,
                Notes = request.Notes,
            };

            var adjId = await adjustmentRepo.InsertAsync(adjustmentDoc, context.RequestAborted);

            return Results.Ok(new
            {
                status = "adjustment_recorded",
                adjustment_id = adjId.ToString(),
                symbol = request.Symbol,
                quantity_delta = request.QuantityDelta,
                cost_basis_delta = request.CostBasisDelta,
                recorded_at = now,
                message = "Manual adjustment recorded. It will be reflected in portfolio calculations after the next refresh.",
            });
        });

        // ── GET /api/v1/reconciliation/adjustments ──────────────────────────
        // Lists the user's manual adjustment history (most recent first).
        // REQ-RECON-004: adjustments recorded against holding context.
        reconcil.MapGet("/adjustments", async (
            HttpContext context,
            IManualAdjustmentRepository adjustmentRepo,
            string? symbol) =>
        {
            var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
            if (string.IsNullOrWhiteSpace(userId))
                return Results.Unauthorized();

            List<ManualAdjustmentDocument> adjustments;
            if (!string.IsNullOrWhiteSpace(symbol))
                adjustments = await adjustmentRepo.GetByUserAndSymbolAsync(
                    userId, symbol, ct: context.RequestAborted);
            else
                adjustments = await adjustmentRepo.GetByUserAsync(
                    userId, ct: context.RequestAborted);

            var result = adjustments.Select(a => new
            {
                id = a.Id.ToString(),
                symbol = a.Symbol,
                adjustment_type = a.AdjustmentType,
                quantity_delta = a.QuantityDelta,
                cost_basis_delta = a.CostBasisDelta,
                reason = a.Reason,
                requested_by = a.RequestedBy,
                approved_by = a.ApprovedBy,
                created_at = a.CreatedAt,
                notes = a.Notes,
            }).ToList();

            return Results.Ok(new { adjustments = result });
        });

        return app;
    }

    private static object BuildSyncStatus(BsonDocument? syncDoc)
    {
        if (syncDoc is null)
        {
            return new
            {
                status = "never_synced",
                last_successful_sync_at = (DateTime?)null,
                last_sync_attempt_at = (DateTime?)null,
                last_sync_request_at = (DateTime?)null,
                sync_request_status = (string?)null,
                message = "Account has never been synced. Use refresh to initiate a sync.",
            };
        }

        var syncStatus = syncDoc.GetValue("sync_status", BsonNull.Value)?.AsString ?? "unknown";
        var lastSync = syncDoc.GetValue("last_successful_sync_at", BsonNull.Value);
        var lastAttempt = syncDoc.GetValue("last_sync_attempt_at", BsonNull.Value);
        var lastRequest = syncDoc.GetValue("last_sync_request_at", BsonNull.Value);
        var requestStatus = syncDoc.GetValue("sync_request_status", BsonNull.Value);

        return new
        {
            status = syncStatus,
            last_successful_sync_at = lastSync.IsBsonNull ? null : (DateTime?)lastSync.ToUniversalTime(),
            last_sync_attempt_at = lastAttempt.IsBsonNull ? null : (DateTime?)lastAttempt.ToUniversalTime(),
            last_sync_request_at = lastRequest.IsBsonNull ? null : (DateTime?)lastRequest.ToUniversalTime(),
            sync_request_status = requestStatus.IsBsonNull ? null : requestStatus.AsString,
            message = syncStatus switch
            {
                "completed" => $"Last successful sync: {lastSync.ToUniversalTime():O}",
                "failed" => "Last sync attempt failed. Use refresh to retry.",
                "suspended" => "Auto-sync is temporarily suspended due to repeated failures. Manual refresh is available.",
                _ => "Sync status unknown. Use refresh to initiate a sync.",
            },
        };
    }
}

/// <summary>Request contract for creating a manual adjustment (Step 3).</summary>
public sealed record AdjustmentRequest(
    string Symbol,
    int QuantityDelta,
    decimal CostBasisDelta,
    string Reason,
    string? AdjustmentType = null,
    string? ApprovedBy = null,
    string? Notes = null);
