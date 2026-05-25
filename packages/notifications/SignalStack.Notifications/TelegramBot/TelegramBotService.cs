using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Audit;
using SignalStack.Domain.Users;

namespace SignalStack.Notifications.TelegramBot;

/// <summary>
/// Core service for admin Telegram bot management.
///
/// REQ-NOTIFY-015: bot provisioning, token validation via Telegram getMe,
/// token replacement with Key Vault write, immediate生效, test-send.
/// REQ-NOTIFY-022a: token rotation monitoring, global-disable
/// disable-and-re-enable cycle on replacement.
/// REQ-NOTIFY-017: Telegram is optional — this service handles the
/// admin-facing bot operations.
/// </summary>
public sealed class TelegramBotService
{
    private const string TelegramApiBase = "https://api.telegram.org/bot";

    private readonly IMongoDatabase _database;
    private readonly ITelegramBotTokenStore _tokenStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAuditEventRepository _auditRepo;
    private readonly IUserRepository _userRepo;
    private readonly IOptions<TelegramBotOptions> _options;
    private readonly ILogger<TelegramBotService> _logger;

    public TelegramBotService(
        IMongoDatabase database,
        ITelegramBotTokenStore tokenStore,
        IHttpClientFactory httpClientFactory,
        IAuditEventRepository auditRepo,
        IUserRepository userRepo,
        IOptions<TelegramBotOptions> options,
        ILogger<TelegramBotService> logger)
    {
        _database = database;
        _tokenStore = tokenStore;
        _httpClientFactory = httpClientFactory;
        _auditRepo = auditRepo;
        _userRepo = userRepo;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Returns the current active bot status, or null if no bot has been provisioned.
    /// REQ-NOTIFY-015: admin portal must display bot username, last delivery timestamp,
    /// and token age as the bot health indicator.
    /// </summary>
    public async Task<TelegramBotStatus?> GetActiveBotStatusAsync(CancellationToken ct = default)
    {
        var active = await FindActiveBotAsync(ct);
        if (active is null)
            return null;

        var token = await _tokenStore.GetTokenAsync(ct);
        var rotationDays = await ReadSysConfigIntAsync(
            "notifications.telegram.rotation_days", 90, ct);

        var tokenAgeDays = active.LastTokenReplacedAt.HasValue
            ? (int)(DateTime.UtcNow - active.LastTokenReplacedAt.Value).TotalDays
            : (int)(DateTime.UtcNow - active.ProvisionedAt).TotalDays;

        return new TelegramBotStatus(
            BotId: active.Id.ToString(),
            BotUsername: active.BotUsername,
            IsActive: active.IsActive,
            HasTokenConfigured: !string.IsNullOrWhiteSpace(token),
            TokenAgeDays: tokenAgeDays,
            RotationDueDays: rotationDays,
            RotationOverdue: tokenAgeDays >= rotationDays,
            LastTokenReplacedAt: active.LastTokenReplacedAt,
            LastSuccessfulDeliveryAt: active.LastSuccessfulDeliveryAt,
            LastDeliveryCheckAt: active.LastDeliveryCheckAt,
            ProvisionedAt: active.ProvisionedAt,
            ProvisionedByUserId: active.ProvisionedByUserId
        );
    }

    /// <summary>
    /// Validates a bot token with Telegram's getMe endpoint and,
    /// on success, stores it and creates/updates the notification_bots record.
    /// REQ-NOTIFY-015: validate via getMe before writing; on success the new
    /// token is used immediately with no grace period.
    /// REQ-NOTIFY-022a(c): disable-and-re-enable cycle — sets global_disable=true
    /// during validation, false on success, leaves true on failure.
    /// </summary>
    public async Task<ProvisionBotResult> ProvisionBotAsync(
        string token,
        string adminUserId,
        CancellationToken ct = default)
    {
        // ── Step 1: Set global_disable=true for the replacement window ───
        // REQ-NOTIFY-022a(c): prevent delivery against a potentially compromised token.
        await SetGlobalDisableAsync(true, ct);

        try
        {
            // ── Step 2: Validate the token with Telegram getMe ────────────
            var validation = await ValidateTokenWithTelegramAsync(token, ct);

            if (!validation.IsValid)
            {
                return new ProvisionBotResult(
                    Success: false,
                    ErrorCode: "token_validation_failed",
                    ErrorDetail: validation.ErrorDetail ?? "Telegram getMe validation failed.");
            }

            // ── Step 3: Get or deactivate existing active bot ─────────────
            var collection = GetBotsCollection();
            var existingActive = await FindActiveBotAsync(ct);

            ObjectId? previousBotId = existingActive?.Id;

            if (existingActive is not null)
            {
                // Deactivate the previous active bot (REQ-NOTIFY-015: retain for audit).
                var deactivateFilter = Builders<TelegramBotDocument>.Filter
                    .Eq(b => b.Id, existingActive.Id);
                var deactivateUpdate = Builders<TelegramBotDocument>.Update
                    .Set(b => b.IsActive, false);
                await collection.UpdateOneAsync(deactivateFilter, deactivateUpdate, cancellationToken: ct);
            }

            // ── Step 4: Write the token to Key Vault (or local dev store) ─
            await _tokenStore.SetTokenAsync(token, adminUserId, ct);

            // ── Step 5: Insert new active bot record ──────────────────────
            var now = DateTime.UtcNow;
            var newBot = new TelegramBotDocument
            {
                BotUsername = validation.Username!,
                IsActive = true,
                ProvisionedByUserId = adminUserId,
                ProvisionedAt = now,
                LastTokenReplacedAt = now,
                PreviousBotId = previousBotId
            };

            await collection.InsertOneAsync(newBot, cancellationToken: ct);

            // ── Step 6: Write audit event (REQ-NOTIFY-015) ────────────────
            await _auditRepo.RecordAsync(
                adminUserId,
                "telegram_bot_token_replaced",
                DateTime.UtcNow,
                details: new Dictionary<string, object?>
                {
                    ["new_bot_id"] = newBot.Id.ToString(),
                    ["new_bot_username"] = validation.Username,
                    ["previous_bot_id"] = previousBotId?.ToString(),
                    ["token_replaced_at"] = now.ToString("o")
                },
                cancellationToken: ct);

            // ── Step 7: Set global_disable=false on success ───────────────
            // REQ-NOTIFY-022a(c): re-enable delivery after successful replacement.
            await SetGlobalDisableAsync(false, ct);

            return new ProvisionBotResult(
                Success: true,
                BotId: newBot.Id.ToString(),
                BotUsername: validation.Username);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to provision Telegram bot token.");

            // REQ-NOTIFY-022a(c): leave global_disable=true on failure.
            // The admin must retry or clear the flag manually.
            return new ProvisionBotResult(
                Success: false,
                ErrorCode: "provisioning_failed",
                ErrorDetail: ex.Message);
        }
    }

    /// <summary>
    /// Validates a bot token by calling Telegram's getMe endpoint.
    /// REQ-NOTIFY-015: token must be validated before writing to Key Vault.
    /// </summary>
    public async Task<TokenValidationResult> ValidateTokenWithTelegramAsync(
        string token, CancellationToken ct = default)
    {
        var httpClient = _httpClientFactory.CreateClient("TelegramBotApi");
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        var getMeUrl = $"{TelegramApiBase}{token}/getMe";

        try
        {
            using var response = await httpClient.GetAsync(getMeUrl, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new TokenValidationResult(
                    IsValid: false,
                    ErrorDetail: $"Telegram API returned {response.StatusCode}: {body}");
            }

            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                return new TokenValidationResult(
                    IsValid: false,
                    ErrorDetail: "Telegram getMe returned ok=false.");
            }

            var username = doc.RootElement
                .GetProperty("result")
                .GetProperty("username")
                .GetString();

            if (string.IsNullOrWhiteSpace(username))
            {
                return new TokenValidationResult(
                    IsValid: false,
                    ErrorDetail: "Telegram getMe returned empty username.");
            }

            return new TokenValidationResult(IsValid: true, Username: username);
        }
        catch (TaskCanceledException)
        {
            return new TokenValidationResult(
                IsValid: false,
                ErrorDetail: "Telegram API request timed out.");
        }
        catch (HttpRequestException ex)
        {
            return new TokenValidationResult(
                IsValid: false,
                ErrorDetail: $"Telegram API request failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a test message to the admin's linked Telegram account.
    /// REQ-NOTIFY-015: test-send must fail visibly if admin has no linked
    /// Telegram account or if delivery fails.
    /// </summary>
    public async Task<TestSendResult> TestSendAsync(
        string adminUserId, CancellationToken ct = default)
    {
        var user = await _userRepo.FindByUserIdAsync(adminUserId, ct);
        if (user is null)
            return new TestSendResult(Success: false, ErrorDetail: "Admin user not found.");

        if (string.IsNullOrWhiteSpace(user.TelegramChatId))
            return new TestSendResult(
                Success: false,
                ErrorDetail: "Admin has no linked Telegram account. Link a Telegram account first.");

        var botToken = await _tokenStore.GetTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(botToken))
            return new TestSendResult(
                Success: false, ErrorDetail: "No bot token configured. Provision a bot first.");

        var httpClient = _httpClientFactory.CreateClient("TelegramBotApi");
        httpClient.Timeout = TimeSpan.FromSeconds(15);

        var sendUrl = $"{TelegramApiBase}{botToken}/sendMessage";
        var payload = new
        {
            chat_id = user.TelegramChatId,
            text = "✅ *Telegram bot test message*\n\nYour Telegram notification channel is operational.",
            parse_mode = "Markdown"
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(sendUrl, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                return new TestSendResult(
                    Success: false,
                    ErrorDetail: $"Telegram API returned {response.StatusCode}: {errorBody}");
            }

            return new TestSendResult(Success: true);
        }
        catch (Exception ex)
        {
            return new TestSendResult(
                Success: false,
                ErrorDetail: $"Telegram API request failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the token rotation information per REQ-NOTIFY-022a.
    /// </summary>
    public async Task<TokenRotationInfo> GetTokenRotationInfoAsync(CancellationToken ct = default)
    {
        var active = await FindActiveBotAsync(ct);
        if (active is null)
            return new TokenRotationInfo(IsRotationDue: false, TokenAgeDays: 0, RotationDays: 90);

        var rotationDays = await ReadSysConfigIntAsync(
            "notifications.telegram.rotation_days", 90, ct);

        var tokenAgeDays = active.LastTokenReplacedAt.HasValue
            ? (int)(DateTime.UtcNow - active.LastTokenReplacedAt.Value).TotalDays
            : (int)(DateTime.UtcNow - active.ProvisionedAt).TotalDays;

        return new TokenRotationInfo(
            IsRotationDue: tokenAgeDays >= rotationDays,
            TokenAgeDays: tokenAgeDays,
            RotationDays: rotationDays);
    }

    /// <summary>
    /// Updates the last successful delivery timestamp on the active bot record.
    /// Called by the NDJ after a successful Telegram dispatch.
    /// </summary>
    public async Task RecordSuccessfulDeliveryAsync(CancellationToken ct = default)
    {
        var active = await FindActiveBotAsync(ct);
        if (active is null) return;

        var collection = GetBotsCollection();
        var filter = Builders<TelegramBotDocument>.Filter.Eq(b => b.Id, active.Id);
        var update = Builders<TelegramBotDocument>.Update
            .Set(b => b.LastSuccessfulDeliveryAt, DateTime.UtcNow);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    // ── Private helpers ──────────────────────────────────────────────────

    private IMongoCollection<TelegramBotDocument> GetBotsCollection()
        => _database.GetCollection<TelegramBotDocument>("notification_bots");

    private async Task<TelegramBotDocument?> FindActiveBotAsync(CancellationToken ct)
    {
        var collection = GetBotsCollection();
        var filter = Builders<TelegramBotDocument>.Filter.Eq(b => b.IsActive, true);
        return await collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    private async Task SetGlobalDisableAsync(bool disabled, CancellationToken ct)
    {
        var sysConfig = _database.GetCollection<MongoDB.Bson.BsonDocument>("sys_config");
        var filter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter
            .Eq("key", "notifications.telegram.global_disable");
        var update = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Update
            .Set("value", disabled ? "true" : "false");

        await sysConfig.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    private async Task<int> ReadSysConfigIntAsync(string key, int defaultValue, CancellationToken ct)
    {
        var sysConfig = _database.GetCollection<MongoDB.Bson.BsonDocument>("sys_config");
        var filter = MongoDB.Driver.Builders<MongoDB.Bson.BsonDocument>.Filter.Eq("key", key);
        var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

        if (doc is null) return defaultValue;
        var value = doc.GetValue("value", MongoDB.Bson.BsonNull.Value);
        if (value.IsBsonNull) return defaultValue;
        if (int.TryParse(value.AsString, out var parsed)) return parsed;
        return defaultValue;
    }
}

// ── Result types ──────────────────────────────────────────────────────────

public sealed record TelegramBotStatus(
    string BotId,
    string BotUsername,
    bool IsActive,
    bool HasTokenConfigured,
    int TokenAgeDays,
    int RotationDueDays,
    bool RotationOverdue,
    DateTime? LastTokenReplacedAt,
    DateTime? LastSuccessfulDeliveryAt,
    DateTime? LastDeliveryCheckAt,
    DateTime ProvisionedAt,
    string ProvisionedByUserId
);

public sealed record ProvisionBotResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorDetail = null,
    string? BotId = null,
    string? BotUsername = null
);

public sealed record TokenValidationResult(
    bool IsValid,
    string? Username = null,
    string? ErrorDetail = null
);

public sealed record TestSendResult(
    bool Success,
    string? ErrorDetail = null
);

public sealed record TokenRotationInfo(
    bool IsRotationDue,
    int TokenAgeDays,
    int RotationDays
);
