using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.SysConfig;

namespace SignalStack.Api.Execution;

/// <summary>
/// Exception thrown when the signed-payload endpoint refuses to sign.
/// Carries a machine-readable <see cref="Reason"/> and the structured
/// <see cref="Error"/> returned to the frontend.
/// </summary>
public sealed class SignedPayloadSigningException(string reason, string? message, DateTime? lastSuccessfulSyncAt = null)
    : Exception(message ?? reason)
{
    public string Reason { get; } = reason;
    public SignedPayloadError Error { get; } = new(reason, message, lastSuccessfulSyncAt);
}

/// <summary>
/// Validates order parameters, applies the multi-intent heat gate (EC-5)
/// and LADS health gate (RME-R2), generates a single-use nonce, and
/// returns an HMAC-signed payload for the &lt;fyers-button&gt; element.
/// P7-T5 / REQ-ORDER-009b/009c.
/// </summary>
public interface IIntentSigningService
{
    /// <summary>
    /// Creates a signed payload for the current user + session.
    /// Refuses to sign if any gate is active or validation fails.
    /// </summary>
    Task<SignedPayloadResponse> CreateSignedPayloadAsync(
        string userId, string sessionId, SignedPayloadRequest request, CancellationToken ct);
}

public sealed class IntentSigningService : IIntentSigningService
{
    private const string DomainSeparationPrefix = "fyers.intent.v1:";
    private const string HmacKeyConfigPath = "Execution:IntentHmacKey";

    private readonly IMongoDatabase _database;
    private readonly ISysConfigRepository _sysConfig;
    private readonly byte[] _hmacKey;

    public IntentSigningService(
        IMongoDatabase database,
        ISysConfigRepository sysConfig,
        IConfiguration configuration)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _sysConfig = sysConfig ?? throw new ArgumentNullException(nameof(sysConfig));

        var keyString = configuration[HmacKeyConfigPath]
            ?? throw new InvalidOperationException(
                $"{HmacKeyConfigPath} configuration is required. "
                + "In production this is sourced from Azure Key Vault via the secret name "
                + "recorded in sys_config 'orders.intent.hmac_key_name'.");
        _hmacKey = Encoding.UTF8.GetBytes(keyString);
    }

    public async Task<SignedPayloadResponse> CreateSignedPayloadAsync(
        string userId, string sessionId, SignedPayloadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        // ── 1. Validate action ──────────────────────────────────────────────
        var action = request.Action.ToLowerInvariant();
        var validActions = new[] { "entry", "add", "reduce", "exit" };
        if (!validActions.Contains(action))
            ThrowSigning("invalid_action", $"Action must be one of: {string.Join(", ", validActions)}.");

        // ── 2. Determine transaction type ───────────────────────────────────
        // BUY for Entry and Add; SELL for Reduce and Exit.
        var transactionType = action is "entry" or "add" ? "BUY" : "SELL";

        // For SELL actions, verify the user has an existing long position.
        if (transactionType == "SELL")
            await VerifyPositionExistsAsync(userId, request.Symbol, action, ct);

        // ── 3. Validate product type (always CNC) ───────────────────────────
        // REQ-ORDER-010: CNC is not user-configurable.
        const string productType = "CNC";

        // ── 4. Validate quantity ────────────────────────────────────────────
        if (request.Quantity <= 0)
            ThrowSigning("invalid_quantity", "Quantity must be a positive integer.");

        // ── 5. Validate order type + price ─────────────────────────────────
        var orderType = request.OrderType.ToUpperInvariant();
        if (orderType is not "MARKET" and not "LIMIT")
            ThrowSigning("invalid_order_type", "Order type must be MARKET or LIMIT.");

        if (orderType == "LIMIT" && (request.LimitPrice is null || request.LimitPrice <= 0))
            ThrowSigning("invalid_price", "Limit price is required and must be greater than zero for LIMIT orders.");

        // ── 6. Re-check hard-block conditions ───────────────────────────────
        // Kill switch (REQ-ORDER-002), drawdown suppression (REQ-ORDER-003),
        // duplicate in-flight (REQ-ORDER-005).
        await CheckHardBlocksAsync(userId, request.Symbol, action, ct);

        // ── 7. Multi-intent portfolio heat gate (EC-5) ──────────────────────
        await CheckHeatGateAsync(userId, request, transactionType, productType, ct);

        // ── 8. LADS health gate (RME-R2) ───────────────────────────────────
        await CheckLadsHealthGateAsync(userId, ct);

        // ── 9. Generate nonce ──────────────────────────────────────────────
        var nonce = GenerateNonce();
        var iat = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // ── 10. Build canonical payload and compute HMAC ────────────────────
        var limitPrice = orderType == "LIMIT" && request.LimitPrice.HasValue
            ? request.LimitPrice.Value.ToString("F2")
            : "";

        var canonicalPayload = string.Join("|",
            DomainSeparationPrefix + nonce,
            userId,
            sessionId,
            request.Symbol,
            action,
            transactionType,
            productType,
            request.Quantity.ToString(),
            orderType,
            limitPrice,
            iat.ToString());

        var hmac = HMACSHA256.HashData(_hmacKey, Encoding.UTF8.GetBytes(canonicalPayload));
        var signature = Convert.ToHexString(hmac).ToLowerInvariant();
        var payloadHash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalPayload)))
            .ToLowerInvariant();

        // ── 11. Read nonce TTL ─────────────────────────────────────────────
        var nonceTtlSeconds = await _sysConfig.GetLongAsync(
            "orders.payload_nonce_ttl_seconds", 300L, ct);
        var expiresAt = iat + nonceTtlSeconds;

        // ── 12. Build data-* attributes for <fyers-button> ──────────────────
        var price = orderType == "LIMIT" && request.LimitPrice.HasValue
            ? request.LimitPrice.Value.ToString("F2")
            : "0";

        var dataAttributes = new Dictionary<string, string>
        {
            ["data-symbol"] = request.Symbol,
            ["data-product"] = productType,
            ["data-quantity"] = request.Quantity.ToString(),
            ["data-transaction_type"] = transactionType,
            ["data-order_type"] = orderType,
            ["data-price"] = price,
            ["data-nonce"] = nonce,
            ["data-signature"] = signature,
        };

        return new SignedPayloadResponse(
            Nonce: nonce,
            DataAttributes: dataAttributes,
            PayloadHash: payloadHash,
            ExpiresAtUnix: expiresAt
        );
    }

    // ── Hard-block re-checks ──────────────────────────────────────────────────

    private async Task CheckHardBlocksAsync(string userId, string symbol, string action, CancellationToken ct)
    {
        // Kill switch
        var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
        var ksDoc = await sysConfig
            .Find(Builders<BsonDocument>.Filter.Eq("key", "risk.kill_switch.active"))
            .FirstOrDefaultAsync(ct);
        var isActive = ksDoc is not null
            && ksDoc.Contains("value")
            && ksDoc["value"] is BsonBoolean b
            && b.Value;
        if (isActive)
            ThrowSigning("kill_switch_active",
                "Global kill switch is active. All trading operations are suspended.");

        // Drawdown suppression for entry/add actions
        if (action is "entry" or "add")
        {
            var equity = await ReadEquityAsync(userId, ct);
            if (equity > 0)
            {
                var hwm = await ReadHighWaterMarkAsync(userId, equity, ct);
                var drawdownPct = hwm > 0 && equity < hwm
                    ? (hwm - equity) / hwm * 100m
                    : 0m;

                var suppressEntryPct = await _sysConfig.GetDecimalAsync(
                    "risk.drawdown_enforcement.suppress_entry_pct", 20m, ct);

                if (drawdownPct >= suppressEntryPct)
                    ThrowSigning("drawdown_suppressed",
                        $"New entries are suppressed — drawdown at {drawdownPct:F1}% (threshold: {suppressEntryPct}%).");
            }
        }

        // Duplicate in-flight for entry actions
        if (action == "entry")
        {
            var positions = _database.GetCollection<BsonDocument>("positions");
            var existing = await positions
                .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                    & Builders<BsonDocument>.Filter.Eq("symbol", symbol)
                    & Builders<BsonDocument>.Filter.Eq("state", "PendingEntry"))
                .FirstOrDefaultAsync(ct);

            if (existing is not null)
                ThrowSigning("duplicate_in_flight",
                    $"An entry for {symbol} is already being processed.");
        }
    }

    // ── Multi-intent portfolio heat gate (EC-5) ──────────────────────────────

    private async Task CheckHeatGateAsync(string userId, SignedPayloadRequest request,
        string transactionType, string productType, CancellationToken ct)
    {
        var equity = await ReadEquityAsync(userId, ct);
        if (equity <= 0) return;

        // Get existing portfolio heat from open positions
        var positions = _database.GetCollection<BsonDocument>("positions");
        var openPositions = await positions
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                & Builders<BsonDocument>.Filter.In("state", new[] { "Open", "Suspended" }))
            .ToListAsync(ct);

        var existingRisk = openPositions.Sum(p =>
        {
            var risk = p.GetValue("risk_amount", BsonNull.Value);
            return !risk.IsBsonNull && risk.IsDecimal128 ? (decimal)risk.AsDecimal128 : 0m;
        });

        // Get pending intents' estimated heat contribution
        var intentLedger = _database.GetCollection<BsonDocument>("intent_ledger");
        var pendingIntents = await intentLedger
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                & Builders<BsonDocument>.Filter.Eq("status", "pending"))
            .ToListAsync(ct);

        var pendingRisk = pendingIntents.Sum(i =>
        {
            var qty = i.GetValue("quantity_requested", BsonNull.Value) switch
            {
                BsonInt32 n => (decimal)n.Value,
                BsonInt64 n => (decimal)n.Value,
                BsonDecimal128 d => (decimal)d.Value,
                _ => 0m
            };
            // Estimate risk as qty × estimated_price × 5% (default assumed risk)
            var estPrice = i.GetValue("estimated_price", BsonNull.Value) switch
            {
                BsonDecimal128 d => (decimal)d.Value,
                BsonDouble d => (decimal)d.Value,
                _ => 0m
            };
            return qty * estPrice * 0.05m;
        });

        // Estimate proposed order's heat contribution
        // For BUY (entry/add) actions: additional risk to portfolio
        // For SELL (reduce/exit) actions: reduces risk (not counted)
        var proposedRisk = transactionType == "BUY"
            ? request.Quantity * 1000m * 0.05m // Default 5% assumed risk on ~₹1000/share
            : 0m;

        var totalRisk = existingRisk + pendingRisk + proposedRisk;
        var totalHeatPct = totalRisk / equity * 100m;

        var maxHeatPct = await _sysConfig.GetDecimalAsync(
            "risk.default.max_portfolio_heat_pct", 5m, ct);

        if (totalHeatPct > maxHeatPct)
        {
            var pendingCount = pendingIntents.Count;
            ThrowSigning("pending_intents_would_breach_heat",
                pendingCount > 0
                    ? $"You have {pendingCount} order(s) submitted to FYERS awaiting confirmation. "
                      + $"If all fill, adding this position would exceed your portfolio heat limit. "
                      + $"Please wait for fill confirmation or cancel in-flight orders before proceeding."
                    : "Adding this position would exceed your portfolio heat limit. "
                      + "Consider reducing existing positions before proceeding.");
        }
    }

    // ── LADS health gate (RME-R2) ────────────────────────────────────────────

    private async Task CheckLadsHealthGateAsync(string userId, CancellationToken ct)
    {
        var incidents = _database.GetCollection<BsonDocument>("rme_incidents");

        var failure = await incidents
            .Find(Builders<BsonDocument>.Filter.Eq("incident_type", "lads_sustained_failure")
                & Builders<BsonDocument>.Filter.Eq("status", "open"))
            .FirstOrDefaultAsync(ct);

        if (failure is not null)
        {
            // Fetch the last successful LADS sync timestamp
            var sync = _database.GetCollection<BsonDocument>("fyers_account_sync");
            var lastSync = await sync
                .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId))
                .FirstOrDefaultAsync(ct);

            var lastSuccessfulSyncAt = lastSync?.GetValue("last_successful_sync_at", BsonNull.Value) switch
            {
                BsonDateTime dt => dt.ToUniversalTime(),
                _ => (DateTime?)null
            };

            ThrowSigning("lads_sustained_failure_active",
                "Account data may be stale — the last portfolio sync encountered repeated failures. "
                + "Position sizing may be based on outdated equity. Consider retrying after the sync recovers.",
                lastSuccessfulSyncAt);
        }
    }

    // ── Position existence verification ──────────────────────────────────────

    private async Task VerifyPositionExistsAsync(string userId, string symbol, string action, CancellationToken ct)
    {
        var positions = _database.GetCollection<BsonDocument>("positions");
        var position = await positions
            .Find(Builders<BsonDocument>.Filter.Eq("user_id", userId)
                & Builders<BsonDocument>.Filter.Eq("symbol", symbol)
                & Builders<BsonDocument>.Filter.Ne("state", "Closed"))
            .FirstOrDefaultAsync(ct);

        if (position is null)
            ThrowSigning("no_position",
                $"Cannot {action} — no open position found for {symbol}.");
    }

    // ── Nonce generation ─────────────────────────────────────────────────────

    private static string GenerateNonce()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    // ── Equity helpers (consistent with PreFlightCheckService) ──────────────

    private async Task<decimal> ReadEquityAsync(string userId, CancellationToken ct)
    {
        var equityCurve = _database.GetCollection<BsonDocument>("equity_curve");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);

        var eqOptions = new FindOptions<BsonDocument, BsonDocument>
        {
            Sort = Builders<BsonDocument>.Sort.Descending("session_date"),
            Limit = 1,
        };

        using var eqCursor = await equityCurve.FindAsync(filter, eqOptions, ct);
        BsonDocument? latest = null;
        if (await eqCursor.MoveNextAsync(ct))
            latest = eqCursor.Current.FirstOrDefault();

        if (latest is not null)
        {
            var val = latest.GetValue("equity_value", BsonNull.Value);
            if (!val.IsBsonNull && val.IsDecimal128)
                return (decimal)val.AsDecimal128;
        }

        var snapshots = _database.GetCollection<BsonDocument>("portfolio_snapshots");
        using var snapCursor = await snapshots.FindAsync(filter, cancellationToken: ct);
        BsonDocument? snap = null;
        if (await snapCursor.MoveNextAsync(ct))
            snap = snapCursor.Current.FirstOrDefault();

        if (snap is not null)
        {
            var mv = snap.GetValue("current_market_value", BsonNull.Value);
            var cash = snap.GetValue("cash_reserve", BsonNull.Value);
            var mvVal = !mv.IsBsonNull && mv.IsDecimal128 ? (decimal)mv.AsDecimal128 : 0m;
            var cashVal = !cash.IsBsonNull && cash.IsDecimal128 ? (decimal)cash.AsDecimal128 : 0m;
            return mvVal + cashVal;
        }

        return 0m;
    }

    private async Task<decimal> ReadHighWaterMarkAsync(string userId, decimal currentEquity, CancellationToken ct)
    {
        var equityCurve = _database.GetCollection<BsonDocument>("equity_curve");
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", userId);

        var options = new FindOptions<BsonDocument, BsonDocument>
        {
            Sort = Builders<BsonDocument>.Sort.Descending("equity_value"),
            Limit = 1,
        };

        using var cursor = await equityCurve.FindAsync(filter, options, ct);
        BsonDocument? highest = null;
        if (await cursor.MoveNextAsync(ct))
            highest = cursor.Current.FirstOrDefault();

        if (highest is not null)
        {
            var val = highest.GetValue("equity_value", BsonNull.Value);
            if (!val.IsBsonNull && val.IsDecimal128)
                return Math.Max((decimal)val.AsDecimal128, currentEquity);
        }

        return currentEquity;
    }

    // ── Exception helper ─────────────────────────────────────────────────────

    private static void ThrowSigning(string reason, string? message, DateTime? lastSuccessfulSyncAt = null)
        => throw new SignedPayloadSigningException(reason, message, lastSuccessfulSyncAt);
}
