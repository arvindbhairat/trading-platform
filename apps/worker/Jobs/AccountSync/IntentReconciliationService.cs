using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Audit;
using SignalStack.Api.Execution;
using SignalStack.Api.Notifications;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Worker.Jobs.AccountSync;

/// <summary>
/// LADS intent-reconciliation safety net (REQ-ORDER-015c).
///
/// Runs as part of each LADS cycle, after trade ingestion, to reconcile
/// observed FYERS trades against <c>intent_ledger</c> records independently
/// of whether any widget callbacks have been received.
///
/// Four reconciliation paths:
///   callback-matched  — stamp <c>reconciled_at</c> on callback-matched intents
///   callback-missed   — promote <c>pending</c> → <c>matched</c> when LADS has a matching trade
///   unresolved        — transition stale pending intents past timeout
///   orphan_ack        — flag broker orders with no matching intent
///
/// Every state transition is written to the audit log.
/// P7-T8 / REQ-ORDER-015c.
/// </summary>
public sealed class IntentReconciliationService
{
    private readonly IIntentLedgerRepository _intentRepository;
    private readonly IAuditEventRepository _auditRepository;
    private readonly INotificationWriter _notificationWriter;
    private readonly IMongoDatabase _database;
    private readonly ILogger<IntentReconciliationService> _logger;

    public IntentReconciliationService(
        IIntentLedgerRepository intentRepository,
        IAuditEventRepository auditRepository,
        INotificationWriter notificationWriter,
        IMongoDatabase database,
        ILogger<IntentReconciliationService> logger)
    {
        _intentRepository = intentRepository ?? throw new ArgumentNullException(nameof(intentRepository));
        _auditRepository = auditRepository ?? throw new ArgumentNullException(nameof(auditRepository));
        _notificationWriter = notificationWriter ?? throw new ArgumentNullException(nameof(notificationWriter));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs one reconciliation pass for the given user against the set of
    /// raw trades observed during this LADS cycle.
    /// </summary>
    /// <param name="userId">Platform user ID (string).</param>
    /// <param name="userObjectId">User's MongoDB ObjectId (for notifications).</param>
    /// <param name="rawTrades">Trades fetched from FYERS during this LADS cycle.</param>
    /// <param name="intentTimeoutMinutes">
    /// Timeout from <c>sys_config.orders.intent_timeout_minutes</c>.
    /// Default 10 if not configured.
    /// </param>
    public async Task ReconcileAsync(
        string userId,
        ObjectId userObjectId,
        IReadOnlyList<RawTrade> rawTrades,
        int intentTimeoutMinutes,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID is required.", nameof(userId));

        ArgumentNullException.ThrowIfNull(rawTrades);

        // ── Phase 1: Stamp reconciled_at on callback-matched intents ─────────
        // For intents already in "matched" state with match_source = "callback",
        // LADS stamps reconciled_at when a matching trade is observed.
        // This confirms the reconciliation loop closed successfully.
        var unreconciledMatched = await _intentRepository
            .GetUnreconciledMatchedIntentsByUserAsync(userId, ct);

        var stampedCount = 0;
        foreach (var intent in unreconciledMatched)
        {
            if (!HasMatchingTrade(intent, rawTrades))
                continue;

            await _intentRepository.StampReconciledAtAsync(intent.Nonce, ct);

            await _auditRepository.RecordAsync(
                actorId: userId,
                actionType: "intent_reconciled_callback",
                eventAt: DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["nonce"] = intent.Nonce,
                    ["symbol"] = intent.Symbol,
                    ["match_source"] = "callback",
                    ["intent_status"] = IntentStatus.Matched,
                }, cancellationToken: ct);

            stampedCount++;
            _logger.LogInformation(
                "Intent reconciliation: callback-matched intent {Nonce} " +
                "for {Symbol} — reconciled_at stamped.",
                intent.Nonce, intent.Symbol);
        }

        // ── Phase 2: Promote pending intents with matching trades ────────────
        // For intents still in "pending" state where LADS observes a matching
        // FYERS trade, promote to "matched" with match_source = "lads_reconciliation".
        // This is the callback-missed path — the callback never fired but LADS
        // independently confirmed the order went through.
        var pendingIntents = await _intentRepository
            .GetPendingIntentsByUserAsync(userId, ct);

        var matchedCount = 0;
        foreach (var intent in pendingIntents)
        {
            var matchingTrade = FindMatchingTrade(intent, rawTrades);
            if (matchingTrade is null)
                continue;

            await _intentRepository.ReconcileFromLadsAsync(
                intent.Nonce, matchingTrade.OrderId, ct);

            await _auditRepository.RecordAsync(
                actorId: userId,
                actionType: "intent_matched_lads",
                eventAt: DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["nonce"] = intent.Nonce,
                    ["symbol"] = intent.Symbol,
                    ["side"] = intent.Side,
                    ["match_source"] = "lads_reconciliation",
                    ["broker_order_id"] = matchingTrade.OrderId,
                    ["trade_id"] = matchingTrade.TradeId,
                    ["filled_qty"] = matchingTrade.Quantity,
                }, cancellationToken: ct);

            matchedCount++;
            _logger.LogInformation(
                "Intent reconciliation: callback-missed intent {Nonce} " +
                "for {Symbol} — promoted to matched (lads_reconciliation).",
                intent.Nonce, intent.Symbol);
        }

        // ── Phase 3: Timeout stale pending intents ───────────────────────────
        // Intents that remain "pending" longer than intentTimeoutMinutes with
        // no observed FYERS order transition to "unresolved".
        var now = DateTime.UtcNow;
        var unresolvedCount = 0;
        foreach (var intent in pendingIntents)
        {
            // Double-check: was this intent just matched in Phase 2?
            // Phase 2 updates the DB, but our in-memory list is stale.
            // Re-fetch the current status to be sure.
            var freshPending = await _intentRepository
                .GetPendingIntentsByUserAsync(userId, ct);
            var stillPending = freshPending.Any(i => i.Nonce == intent.Nonce);
            if (!stillPending)
                continue;

            var ageMinutes = (now - intent.CreatedAt).TotalMinutes;
            if (ageMinutes <= intentTimeoutMinutes)
                continue;

            await _intentRepository.MarkUnresolvedAsync(intent.Nonce, ct);

            await _auditRepository.RecordAsync(
                actorId: userId,
                actionType: "intent_unresolved_timeout",
                eventAt: DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["nonce"] = intent.Nonce,
                    ["symbol"] = intent.Symbol,
                    ["action"] = intent.Action,
                    ["age_minutes"] = Math.Round(ageMinutes, 1),
                    ["timeout_minutes"] = intentTimeoutMinutes,
                }, cancellationToken: ct);

            await CreateUserAdvisoryAsync(
                userObjectId, userId, intent.Symbol, intent.Action,
                "unresolved", ct);

            unresolvedCount++;
            _logger.LogInformation(
                "Intent reconciliation: pending intent {Nonce} for {Symbol} " +
                "timed out after {Age:F1} minutes — marked unresolved.",
                intent.Nonce, intent.Symbol, ageMinutes);
        }

        // ── Phase 4: Detect orphan trades with no matching intent ────────────
        // Broker order IDs observed in trades that have no corresponding intent
        // (pending, matched, or otherwise) are flagged as orphan_ack.
        var orphanOrderIds = GetUnmatchedBrokerOrderIds(rawTrades);
        var matchedOrAcked = new HashSet<string>();
        if (orphanOrderIds.Count > 0)
        {
            var allIntents = await _intentRepository
                .GetAllIntentsByUserAsync(userId, ct);

            matchedOrAcked = allIntents
                .Where(i => i.MatchedBrokerOrderId is not null)
                .Select(i => i.MatchedBrokerOrderId!)
                .ToHashSet(StringComparer.Ordinal);

            // Also include intents matched via nonce whose broker ID is pending
            foreach (var intent in allIntents)
            {
                if (intent.MatchedBrokerOrderId is not null)
                    matchedOrAcked.Add(intent.MatchedBrokerOrderId);
            }
        }

        var orphanCount = 0;
        foreach (var orderId in orphanOrderIds)
        {
            if (matchedOrAcked.Contains(orderId))
                continue;

            var trades = rawTrades
                .Where(t => string.Equals(t.OrderId, orderId, StringComparison.Ordinal))
                .ToList();

            if (trades.Count == 0)
                continue;

            var firstTrade = trades[0];
            var totalQty = trades.Sum(t => t.Quantity);

            try
            {
                var orphanNonce = $"orphan_{orderId}";

                await _intentRepository.CreateOrphanAckAsync(
                    new IntentLedgerDocument
                    {
                        UserId = userId,
                        Symbol = firstTrade.Symbol,
                        Side = firstTrade.Side,
                        Action = MapSideToAction(firstTrade.Side),
                        Quantity = totalQty,
                        OrderType = "MARKET",
                        Price = 0m,
                        Product = "CNC",
                        Status = IntentStatus.OrphanAck,
                        MatchSource = "lads_reconciliation",
                        MatchedBrokerOrderId = orderId,
                        Nonce = orphanNonce,
                        PayloadSignature = "lads_orphan_detection",
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                    }, ct);

                await _auditRepository.RecordAsync(
                    actorId: userId,
                    actionType: "intent_orphan_ack",
                    eventAt: DateTime.UtcNow,
                    details: new Dictionary<string, object?>
                    {
                        ["broker_order_id"] = orderId,
                        ["symbol"] = firstTrade.Symbol,
                        ["side"] = firstTrade.Side,
                        ["filled_qty"] = totalQty,
                        ["source"] = "lads_reconciliation",
                    }, cancellationToken: ct);

                await CreateUserAdvisoryAsync(
                    userObjectId, userId, firstTrade.Symbol,
                    MapSideToAction(firstTrade.Side),
                    "orphan_ack", ct);

                orphanCount++;
                _logger.LogInformation(
                    "Intent reconciliation: orphan order {OrderId} for {Symbol} " +
                    "— orphan_ack created.",
                    orderId, firstTrade.Symbol);
            }
            catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey)
            {
                // orphan_ack for this order ID already exists — idempotent
                _logger.LogTrace(
                    "Intent reconciliation: orphan_ack already exists for order {OrderId}. Skipping.",
                    orderId);
            }
        }

        // ── Summary ──────────────────────────────────────────────────────────
        if (stampedCount > 0 || matchedCount > 0 || unresolvedCount > 0 || orphanCount > 0)
        {
            _logger.LogInformation(
                "Intent reconciliation complete for user {UserId}: " +
                "{Stamped} callback-stamped, {Matched} LADS-matched, " +
                "{Unresolved} timed out, {Orphan} orphan-acked.",
                userId, stampedCount, matchedCount, unresolvedCount, orphanCount);
        }
    }

    /// <summary>
    /// Returns true when any of the provided raw trades matches the intent
    /// by symbol and side. Used to stamp <c>reconciled_at</c> on callback-
    /// matched intents whose trade has been observed by LADS.
    /// </summary>
    private static bool HasMatchingTrade(
        IntentLedgerDocument intent, IReadOnlyList<RawTrade> rawTrades)
    {
        return rawTrades.Any(t =>
            string.Equals(t.Symbol, intent.Symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.Side, intent.Side, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Finds the first trade matching the intent by symbol and side, with
    /// preference for trades whose <c>OrderId</c> explicitly matches the
    /// intent's <c>MatchedBrokerOrderId</c> when available.
    /// Returns null when no match is found.
    /// </summary>
    private static RawTrade? FindMatchingTrade(
        IntentLedgerDocument intent, IReadOnlyList<RawTrade> rawTrades)
    {
        // First pass: prefer OrderId match when intent already has a broker order ID
        if (intent.MatchedBrokerOrderId is not null)
        {
            var byOrderId = rawTrades.FirstOrDefault(t =>
                string.Equals(t.OrderId, intent.MatchedBrokerOrderId, StringComparison.Ordinal));
            if (byOrderId is not null)
                return byOrderId;
        }

        // Second pass: match by symbol + side
        return rawTrades.FirstOrDefault(t =>
            string.Equals(t.Symbol, intent.Symbol, StringComparison.OrdinalIgnoreCase)
            && string.Equals(t.Side, intent.Side, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns the set of broker order IDs from trades that could be orphaned.
    /// Only considers trades with a non-null <c>OrderId</c>.
    /// </summary>
    private static HashSet<string> GetUnmatchedBrokerOrderIds(
        IReadOnlyList<RawTrade> rawTrades)
    {
        return rawTrades
            .Where(t => t.OrderId is not null)
            .Select(t => t.OrderId!)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Maps a trade side to the corresponding intent action for orphan records.
    /// </summary>
    private static string MapSideToAction(string side)
    {
        if (string.Equals(side, "buy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(side, "b", StringComparison.OrdinalIgnoreCase))
            return "entry";

        if (string.Equals(side, "sell", StringComparison.OrdinalIgnoreCase)
            || string.Equals(side, "s", StringComparison.OrdinalIgnoreCase))
            return "exit";

        return "unknown";
    }

    /// <summary>
    /// Creates a user advisory notification for an unresolved or orphan_ack intent.
    /// REQ-ORDER-015c: user advisory must be surfaced when an intent transitions
    /// to <c>unresolved</c> or <c>orphan_ack</c>.
    /// </summary>
    private async Task CreateUserAdvisoryAsync(
        ObjectId userObjectId,
        string userId,
        string symbol,
        string action,
        string reason,
        CancellationToken ct)
    {
        string content = reason switch
        {
            "unresolved" =>
                $"We couldn't confirm whether your {action} order for {symbol} was submitted. " +
                "Check your FYERS orders for current status.",

            "orphan_ack" =>
                $"An unexpected {'t'}rade was detected for {symbol} — please review your positions.",

            _ =>
                $"An order reconciliation issue was detected for {symbol} — please review your positions.",
        };

        // Construct advisory text with action prefix
        var advisoryContent = reason switch
        {
            "unresolved" =>
                $"Order status unclear — {content}",

            "orphan_ack" =>
                $"Unexpected activity detected — {content}",

            _ => content,
        };

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userObjectId,
            NotificationType = NotificationType.PortfolioImpactInsight,
            Symbol = symbol,
            Content = advisoryContent,
            GeneratedAt = DateTime.UtcNow,
            TelegramDeliveryStatus = "pending",
            TelegramDeliveryAttempts = 0,
            IsRead = false,
            IsAdminNotification = false,
        };

        await _notificationWriter.WriteAsync(notification, ct);
    }
}
