using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Fyers;
using SignalStack.Api.Notifications;

namespace SignalStack.Worker.Integrations.Fyers;

/// <summary>
/// Monitors the shared-ingestion admin FYERS token and maintains a degraded-mode
/// state. When the admin token is unavailable (missing, expired, or dirty),
/// shared-ingestion jobs (LMDS, DS, EODSR) must suspend; user-token paths (LADS,
/// PLD quotes, charts) continue unaffected.
///
/// State is mirrored to <c>sys_config</c> so the API tier can serve the admin
/// banner without coupling to the worker process (REQ-MARKET-016).
/// </summary>
public sealed class SharedTokenHealthService
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<SharedTokenHealthService> _logger;

    private readonly object _lock = new();
    private bool _isDegraded;
    private DateTime? _lastSuccessfulTokenAt;
    private bool _alertFired;
    private string? _degradedReason;

    private const string SysConfigDegradedKey = "ops.degraded_mode.shared_ingestion_token";
    private const string SysConfigLastTokenKey = "ops.degraded_mode.last_successful_token_at";
    private const string SysConfigDegradedReasonKey = "ops.degraded_mode.degraded_reason";

    public SharedTokenHealthService(
        IMongoDatabase database,
        ILogger<SharedTokenHealthService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Whether the shared-ingestion token is currently degraded.</summary>
    public bool IsDegraded
    {
        get { lock (_lock) return _isDegraded; }
    }

    /// <summary>Returns a snapshot of the current health state.</summary>
    public SharedTokenHealth CurrentHealth
    {
        get
        {
            lock (_lock)
                return new SharedTokenHealth(_isDegraded, _lastSuccessfulTokenAt, _degradedReason, _alertFired);
        }
    }

    /// <summary>
    /// Returns the current admin token string, or null if unavailable.
    /// This is used as the token provider for the FYERS MDP adapter.
    /// </summary>
    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        try
        {
            var fyersTokens = _database.GetCollection<FyersTokenDocument>("fyers_tokens");
            var adminToken = await fyersTokens
                .Find(t => t.UserId == "admin"
                    && t.Status == FyersTokenStatus.Active
                    && t.ExpiresAt > DateTime.UtcNow)
                .FirstOrDefaultAsync(ct);

            if (adminToken is null)
            {
                await TransitionToDegradedAsync("admin_token_not_found", null, ct);
                return null;
            }

            await TransitionToHealthyAsync(adminToken.ExpiresAt, ct);
            return adminToken.AccessTokenRef;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve admin FYERS token from fyers_tokens collection.");
            await TransitionToDegradedAsync("admin_token_read_error", null, ct);
            return null;
        }
    }

    /// <summary>
    /// Checks admin token health in sys_config. Returns true if healthy (non-degraded).
    /// Lightweight check for workers that only need the degraded flag, not the token.
    /// </summary>
    public async Task<bool> IsTokenHealthyAsync(CancellationToken ct = default)
    {
        var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
        var degradedDoc = await sysConfig
            .Find(Builders<BsonDocument>.Filter.Eq("_id", SysConfigDegradedKey))
            .FirstOrDefaultAsync(ct);

        if (degradedDoc is null)
            return true; // absent = presumed healthy

        var value = degradedDoc.GetValue("value", BsonNull.Value)?.AsString;
        lock (_lock) _isDegraded = value == "true";

        return !_isDegraded;
    }

    /// <summary>
    /// Marks the alert as fired so it is not re-sent on subsequent poll cycles.
    /// </summary>
    public void MarkAlertFired()
    {
        lock (_lock) _alertFired = true;
    }

    /// <summary>
    /// Returns true if this is the first detection of the degraded state
    /// (alert has not yet been fired for this degradation episode).
    /// </summary>
    public bool ShouldFireAlert()
    {
        lock (_lock) return _isDegraded && !_alertFired;
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private async Task TransitionToDegradedAsync(string reason, DateTime? lastKnownGood, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_isDegraded)
                return; // already degraded — no state change

            _isDegraded = true;
            _degradedReason = reason;
            _lastSuccessfulTokenAt = lastKnownGood ?? _lastSuccessfulTokenAt;
        }

        _logger.LogWarning(
            "Shared-ingestion token degraded: {Reason}. " +
            "LMDS, DS, and EODSR will suspend until token is restored (REQ-MARKET-016).",
            reason);

        // ── One-time admin notification + Telegram alert (REQ-MARKET-016(g)) ──
        // Written only on the first degradation; the early-return above prevents
        // re-firing on subsequent poll cycles within the same degradation episode.
        try
        {
            var notifications = _database.GetCollection<BsonDocument>("notifications");
            var lastTokenStr = _lastSuccessfulTokenAt?.ToString("o") ?? "unknown";

            var content =
                "🚨 Shared-ingestion suspended: admin FYERS token refresh failed. " +
                "LMDS and DataSync are halted. " +
                $"Last successful token at {lastTokenStr}. " +
                "Re-authenticate to restore. " +
                "User-account paths (LADS, PLD quotes, charts, exits) are unaffected.";

            var notification = new BsonDocument
            {
                ["notification_type"] = NotificationType.AdminSharedIngestionTokenLost,
                ["is_admin_notification"] = true,
                ["content"] = content,
                ["deep_link"] = "/admin/settings/fyers-token",
                ["generated_at"] = DateTime.UtcNow,
                ["telegram_delivery_status"] = "pending",
                ["telegram_delivery_attempts"] = 0,
                ["email_delivery_status"] = "not_attempted",
                ["email_attempts"] = 0,
            };

            await notifications.InsertOneAsync(notification, cancellationToken: ct);

            _logger.LogWarning(
                "Admin shared-ingestion token lost notification written " +
                "(one-time alert per degradation episode).");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write admin shared-ingestion token lost notification.");
        }

        await PersistDegradedStateAsync(ct);
    }

    private async Task TransitionToHealthyAsync(DateTime expiresAt, CancellationToken ct)
    {
        lock (_lock)
        {
            if (!_isDegraded && _lastSuccessfulTokenAt.HasValue)
                return; // already healthy — no state change

            var wasDegraded = _isDegraded;
            _isDegraded = false;
            _lastSuccessfulTokenAt = DateTime.UtcNow;
            _alertFired = false;
            _degradedReason = null;

            if (wasDegraded)
            {
                _logger.LogInformation(
                    "Shared-ingestion token restored. LMDS, DS, and EODSR will " +
                    "resume on their next scheduled cycles (REQ-MARKET-016). Expires at {ExpiresAt}.",
                    expiresAt);
            }
        }

        await PersistHealthyStateAsync(ct);
    }

    private async Task PersistDegradedStateAsync(CancellationToken ct)
    {
        try
        {
            var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
            var now = DateTime.UtcNow.ToString("o");

            await sysConfig.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", SysConfigDegradedKey),
                Builders<BsonDocument>.Update.Set("value", "true").Set("updated_at", now),
                new UpdateOptions { IsUpsert = true },
                cancellationToken: ct);

            if (_lastSuccessfulTokenAt.HasValue)
            {
                await sysConfig.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", SysConfigLastTokenKey),
                    Builders<BsonDocument>.Update
                        .Set("value", _lastSuccessfulTokenAt.Value.ToString("o"))
                        .Set("updated_at", now),
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken: ct);
            }

            await sysConfig.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", SysConfigDegradedReasonKey),
                Builders<BsonDocument>.Update
                    .Set("value", _degradedReason ?? "unknown")
                    .Set("updated_at", now),
                new UpdateOptions { IsUpsert = true },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist degraded state to sys_config.");
        }
    }

    private async Task PersistHealthyStateAsync(CancellationToken ct)
    {
        try
        {
            var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
            var now = DateTime.UtcNow.ToString("o");

            await sysConfig.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", SysConfigDegradedKey),
                Builders<BsonDocument>.Update.Set("value", "false").Set("updated_at", now),
                new UpdateOptions { IsUpsert = true },
                cancellationToken: ct);

            if (_lastSuccessfulTokenAt.HasValue)
            {
                await sysConfig.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", SysConfigLastTokenKey),
                    Builders<BsonDocument>.Update
                        .Set("value", _lastSuccessfulTokenAt.Value.ToString("o"))
                        .Set("updated_at", now),
                    new UpdateOptions { IsUpsert = true },
                    cancellationToken: ct);
            }

            await sysConfig.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", SysConfigDegradedReasonKey),
                Builders<BsonDocument>.Update.Set("value", "").Set("updated_at", now),
                new UpdateOptions { IsUpsert = true },
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist healthy state to sys_config.");
        }
    }
}

/// <summary>
/// Immutable snapshot of the shared-ingestion token health state.
/// </summary>
public sealed record SharedTokenHealth(
    bool IsDegraded,
    DateTime? LastSuccessfulTokenAt,
    string? DegradedReason,
    bool AlertFired);
