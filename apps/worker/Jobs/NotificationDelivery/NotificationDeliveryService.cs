using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Notifications;
using SignalStack.Storage.Notifications;
using SignalStack.Notifications.TelegramBot;
using SignalStack.Domain.Users;

namespace SignalStack.Worker.Jobs.NotificationDelivery;

/// <summary>
/// Core service for the Notification Delivery Job (NDJ).
///
/// Sole Telegram dispatcher for all notification types. Reads pending
/// notification records from MongoDB and delivers via the Telegram Bot API.
///
/// REQ-NOTIFY-007:  NDJ is the sole Telegram dispatcher.
/// REQ-NOTIFY-008:  retry with exponential back-off; 429 Retry-After handling.
/// REQ-NOTIFY-019:  dirty-link auto-disable after threshold failures.
/// REQ-NOTIFY-022a: global-disable break-glass flag.
/// REQ-NOTIFY-023:  idempotent delivery via telegram_message_id.
/// </summary>
public sealed class NotificationDeliveryService
{
    private const string TelegramDeliveryPending = "pending";
    private const string TelegramDeliveryDelivered = "delivered";
    private const string TelegramDeliveryFailed = "failed";

    private readonly IMongoDatabase _database;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<NotificationDeliveryOptions> _options;
    private readonly ILogger<NotificationDeliveryService> _logger;

    // Per-bot rate-limit state: when the bot was last 429'd and should back off until.
    private DateTime _botBackoffUntil = DateTime.MinValue;

    public NotificationDeliveryService(
        IMongoDatabase database,
        IHttpClientFactory httpClientFactory,
        IOptions<NotificationDeliveryOptions> options,
        ILogger<NotificationDeliveryService> logger)
    {
        _database = database;
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Executes a single NDJ cycle: fetches pending notifications and dispatches them.
    /// </summary>
    /// <returns>The number of notifications processed (delivered or failed) this cycle.</returns>
    public async Task<int> ExecuteDeliveryCycleAsync(CancellationToken ct = default)
    {
        // ── Step 1: Check global-disable break-glass (REQ-NOTIFY-022a) ───────
        var globalDisable = await ReadGlobalDisableFlagAsync(ct);
        if (globalDisable)
        {
            _logger.LogWarning(
                "NDJ: notifications.telegram.global_disable is true. " +
                "Skipping all Telegram dispatch (REQ-NOTIFY-022a break-glass).");
            return 0;
        }

        // ── Step 2: Resolve the active bot token ─────────────────────────────
        var botToken = await ResolveBotTokenAsync(ct);
        if (string.IsNullOrWhiteSpace(botToken))
        {
            _logger.LogWarning(
                "NDJ: no Telegram bot token resolved. " +
                "Skipping dispatch cycle. Ensure a bot is provisioned (P5-T5).");
            return 0;
        }

        // ── Step 3: Check per-bot rate-limit backoff ─────────────────────────
        if (DateTime.UtcNow < _botBackoffUntil)
        {
            _logger.LogInformation(
                "NDJ: bot is in Retry-After backoff until {BackoffUntil}. Skipping cycle.",
                _botBackoffUntil);
            return 0;
        }

        // ── Step 4: Fetch pending notifications ──────────────────────────────
        var notifications = await GetPendingNotificationsAsync(ct);
        if (notifications.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation(
            "NDJ: processing {Count} pending notifications.", notifications.Count);

        // ── Step 5: Fetch the max_retries and dirty_link_threshold from config ─
        var maxRetries = await ReadSysConfigIntAsync(
            "notifications.telegram.max_retries", 5, ct);
        var dirtyLinkThreshold = await ReadSysConfigIntAsync(
            "notifications.telegram.dirty_link_failure_threshold", 10, ct);

        var processed = 0;

        foreach (var notification in notifications)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var result = await DeliverSingleNotificationAsync(
                    notification, botToken, maxRetries, dirtyLinkThreshold, ct);

                if (result == DeliveryResult.RateLimited)
                {
                    // Bot-level rate limit hit — stop processing this cycle.
                    _logger.LogWarning(
                        "NDJ: bot rate-limited (429). Backing off per Retry-After.");
                    break;
                }

                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "NDJ: unhandled error delivering notification {NotificationId}.",
                    notification.Id);
            }
        }

        _logger.LogInformation(
            "NDJ: cycle complete — {Processed} notifications processed.", processed);
        return processed;
    }

    private enum DeliveryResult
    {
        Success,
        Failed,
        RateLimited
    }

    /// <summary>
    /// Delivers a single notification via the Telegram Bot API.
    /// Handles idempotency, retry, 429 Retry-After, and dirty-link detection.
    /// </summary>
    private async Task<DeliveryResult> DeliverSingleNotificationAsync(
        NotificationDocument notification,
        string botToken,
        int maxRetries,
        int dirtyLinkThreshold,
        CancellationToken ct)
    {
        // ── Idempotency check (REQ-NOTIFY-023) ──────────────────────────────
        if (!string.IsNullOrWhiteSpace(notification.TelegramMessageId))
        {
            // Already delivered — skip.
            _logger.LogTrace(
                "NDJ: notification {NotificationId} already delivered " +
                "(telegram_message_id: {MessageId}). Skipping.",
                notification.Id, notification.TelegramMessageId);
            return DeliveryResult.Success;
        }

        // ── Load the target user and their Telegram chat ID ──────────────────
        var user = await GetUserAsync(notification.UserId, ct);
        if (user is null)
        {
            _logger.LogWarning(
                "NDJ: target user {UserId} not found for notification {NotificationId}. " +
                "Marking as failed.",
                notification.UserId, notification.Id);
            await MarkDeliveryFailedAsync(notification, "Target user not found.", ct);
            return DeliveryResult.Failed;
        }

        var chatId = user.TelegramChatId;
        if (string.IsNullOrWhiteSpace(chatId))
        {
            // User has no linked Telegram account — mark as failed (no chat to send to).
            _logger.LogTrace(
                "NDJ: user {UserId} has no linked Telegram account. " +
                "Skipping notification {NotificationId}.",
                notification.UserId, notification.Id);
            await MarkDeliveryFailedAsync(notification, "No linked Telegram account.", ct);
            return DeliveryResult.Failed;
        }

        // ── Dirty-link check (REQ-NOTIFY-019) ───────────────────────────────
        if (user.TelegramIsDirty)
        {
            _logger.LogTrace(
                "NDJ: Telegram link for user {UserId} is dirty. " +
                "Skipping delivery for notification {NotificationId}.",
                notification.UserId, notification.Id);
            // Don't mark as failed — already known dirty. Leave as pending
            // so the portal feed still shows the notification.
            return DeliveryResult.Failed;
        }

        // ── Check retry limit ───────────────────────────────────────────────
        if (notification.TelegramDeliveryAttempts >= maxRetries)
        {
            _logger.LogWarning(
                "NDJ: notification {NotificationId} has exhausted {MaxRetries} retries. " +
                "Marking as permanently failed.",
                notification.Id, maxRetries);
            await MarkDeliveryFailedAsync(
                notification, $"Exhausted {maxRetries} retries.", ct);
            return DeliveryResult.Failed;
        }

        // ── Dispatch via Telegram Bot API ───────────────────────────────────
        var httpClient = _httpClientFactory.CreateClient("TelegramBotApi");
        httpClient.Timeout = TimeSpan.FromSeconds(
            _options.Value.TelegramApiTimeoutSeconds);

        var apiBaseUrl = _options.Value.TelegramApiBaseUrlTemplate
            .Replace("{BotToken}", botToken);

        var sendMessageUrl = $"{apiBaseUrl}sendMessage";

        var payload = new
        {
            chat_id = chatId,
            text = notification.Content,
            disable_web_page_preview = true,
            parse_mode = "HTML"
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                sendMessageUrl, payload, ct);

            // ── Handle 429 Retry-After (REQ-NOTIFY-008) ─────────────────────
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var retryAfterSeconds = ParseRetryAfter(response.Headers);
                _logger.LogWarning(
                    "NDJ: Telegram API returned 429 for notification {NotificationId}. " +
                    "Retry-After: {RetryAfter}s.",
                    notification.Id, retryAfterSeconds);

                // Set per-bot backoff (REQ-NOTIFY-008: rate limits are at bot level).
                _botBackoffUntil = DateTime.UtcNow.AddSeconds(retryAfterSeconds);

                return DeliveryResult.RateLimited;
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);

                // ── Detect unreachable chat (dirty link) ─────────────────────
                // Telegram returns 400 with "chat not found" or "bot was blocked"
                // when the user has blocked the bot or deleted their account.
                if (IsChatUnreachableError(errorBody))
                {
                    await HandleDirtyLinkAsync(
                        user, notification, dirtyLinkThreshold, errorBody, ct);
                    return DeliveryResult.Failed;
                }

                // Other API error — increment attempt count.
                _logger.LogWarning(
                    "NDJ: Telegram API returned {StatusCode} for notification {NotificationId}: {Error}.",
                    response.StatusCode, notification.Id, errorBody);

                await IncrementDeliveryAttemptAsync(
                    notification, $"API error: {response.StatusCode} - {errorBody}", ct);
                return DeliveryResult.Failed;
            }

            // ── Success: parse response for message_id (REQ-NOTIFY-023) ──────
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            var messageId = ExtractMessageId(responseBody);

            await MarkDeliverySucceededAsync(notification, messageId, user, ct);

            _logger.LogTrace(
                "NDJ: delivered notification {NotificationId} to user {UserId} " +
                "(message_id: {MessageId}).",
                notification.Id, user.Id, messageId ?? "(none)");

            return DeliveryResult.Success;
        }
        catch (TaskCanceledException)
        {
            // HTTP timeout — increment attempt count.
            _logger.LogWarning(
                "NDJ: Telegram API request timed out for notification {NotificationId}.",
                notification.Id);
            await IncrementDeliveryAttemptAsync(
                notification, "Request timed out.", ct);
            return DeliveryResult.Failed;
        }
    }

    /// <summary>
    /// Reads pending notifications where telegram_delivery_status = "pending"
    /// and telegram_delivery_attempts is within retry limits.
    /// </summary>
    private async Task<List<NotificationDocument>> GetPendingNotificationsAsync(
        CancellationToken ct)
    {
        var collection = _database.GetCollection<NotificationDocument>("notifications");
        var batchSize = _options.Value.BatchSize;

        var filter = Builders<NotificationDocument>.Filter.Eq(
            n => n.TelegramDeliveryStatus, TelegramDeliveryPending);

        return await collection
            .Find(filter)
            .SortBy(n => n.GeneratedAt)
            .Limit(batchSize)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Loads a user document by ID.
    /// </summary>
    private async Task<UserDocument?> GetUserAsync(ObjectId userId, CancellationToken ct)
    {
        var collection = _database.GetCollection<UserDocument>("users");
        var filter = Builders<UserDocument>.Filter.Eq(u => u.Id, userId);
        return await collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Reads the global_disable flag from sys_config (REQ-NOTIFY-022a).
    /// </summary>
    private async Task<bool> ReadGlobalDisableFlagAsync(CancellationToken ct)
    {
        var collection = _database.GetCollection<BsonDocument>("sys_config");
        var filter = Builders<BsonDocument>.Filter.Eq("key", "notifications.telegram.global_disable");
        var doc = await collection.Find(filter).FirstOrDefaultAsync(ct);

        if (doc is null)
            return false; // default: not disabled

        var value = doc.GetValue("value", BsonNull.Value);
        if (value.IsBsonNull) return false;

        return string.Equals(value.AsString, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Reads an integer value from sys_config with a fallback default.
    /// </summary>
    private async Task<int> ReadSysConfigIntAsync(string key, int defaultValue, CancellationToken ct)
    {
        var collection = _database.GetCollection<BsonDocument>("sys_config");
        var filter = Builders<BsonDocument>.Filter.Eq("key", key);
        var doc = await collection.Find(filter).FirstOrDefaultAsync(ct);

        if (doc is null) return defaultValue;

        var value = doc.GetValue("value", BsonNull.Value);
        if (value.IsBsonNull) return defaultValue;

        if (int.TryParse(value.AsString, out var parsed))
            return parsed;

        return defaultValue;
    }

    /// <summary>
    /// Resolves the Telegram bot token from configuration (resolved through
    /// the bootstrap config pipeline, which includes Key Vault).
    /// </summary>
    private async Task<string?> ResolveBotTokenAsync(CancellationToken ct)
    {
        // The bot token is resolved through the bootstrap configuration pipeline
        // which loads from Azure App Configuration + Key Vault.
        // During local development, this can be set via the "TelegramBotToken"
        // configuration key (e.g., in appsettings or environment variables).
        //
        // REQ-NOTIFY-015: bot token stored in Key Vault, never in sys_config.
        //
        // For P5-T4, the token is resolved from IConfiguration. The admin
        // provisioning UI (P5-T5) will handle storing and rotating the token.
        //
        // Since this service doesn't have direct access to IConfiguration,
        // we attempt to read from the Key Vault bootstrap snapshot or
        // environment. Returns null if no token is configured.
        //
        // This method uses the configuration bootstrap pipeline's Key Vault
        // snapshot file as a fallback for local development.
        try
        {
            var bootstrapPath = Path.Combine(
                AppContext.BaseDirectory, "bootstrap", "keyvault.local.json");
            if (File.Exists(bootstrapPath))
            {
                var json = await File.ReadAllTextAsync(bootstrapPath, ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("TelegramBotToken", out var token))
                {
                    return token.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "NDJ: could not read Telegram bot token from bootstrap snapshot.");
        }

        // Fall back to environment variable for local dev.
        return Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
    }

    /// <summary>
    /// Parses the Retry-After header from a 429 response.
    /// Returns the number of seconds to back off.
    /// REQ-NOTIFY-008: Retry-After must be respected.
    /// </summary>
    private static int ParseRetryAfter(HttpResponseHeaders headers)
    {
        if (headers.RetryAfter?.Delta is not null)
            return (int)Math.Ceiling(headers.RetryAfter.Delta.Value.TotalSeconds);

        if (headers.RetryAfter?.Date is not null)
        {
            var diff = headers.RetryAfter.Date.Value.UtcDateTime - DateTime.UtcNow;
            return diff.TotalSeconds > 0 ? (int)Math.Ceiling(diff.TotalSeconds) : 5;
        }

        return 5; // default backoff if no Retry-After header
    }

    /// <summary>
    /// Checks whether a Telegram API error body indicates the chat is unreachable
    /// (user blocked the bot, deleted account, etc.).
    /// </summary>
    private static bool IsChatUnreachableError(string errorBody)
    {
        // Telegram error codes for unreachable chats:
        // - "chat not found"
        // - "bot was blocked by the user"
        // - "user is deactivated"
        // - "Forbidden: bot was blocked"
        // - "Forbidden: user is deactivated"
        var lower = errorBody.ToLowerInvariant();
        return lower.Contains("chat not found")
            || lower.Contains("bot was blocked")
            || lower.Contains("user is deactivated")
            || lower.Contains("forbidden");
    }

    /// <summary>
    /// Handles dirty-link detection and auto-disable (REQ-NOTIFY-019).
    /// Increments the consecutive failure counter on the user document.
    /// When the threshold is reached, marks the Telegram link as dirty.
    /// </summary>
    private async Task HandleDirtyLinkAsync(
        UserDocument user,
        NotificationDocument notification,
        int dirtyLinkThreshold,
        string errorBody,
        CancellationToken ct)
    {
        var usersCollection = _database.GetCollection<UserDocument>("users");

        // Increment consecutive failures atomically.
        var increment = Builders<UserDocument>.Update
            .Inc(u => u.TelegramConsecutiveFailures, 1);

        // Get the new value after increment.
        var updated = await usersCollection.FindOneAndUpdateAsync<UserDocument>(
            Builders<UserDocument>.Filter.Eq(u => u.Id, user.Id),
            increment,
            options: new FindOneAndUpdateOptions<UserDocument, UserDocument>
            {
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken: ct);

        var failureCount = updated?.TelegramConsecutiveFailures ?? user.TelegramConsecutiveFailures + 1;

        _logger.LogWarning(
            "NDJ: Telegram chat unreachable for user {UserId} ({FailureCount}/{Threshold}). {Error}",
            user.Id, failureCount, dirtyLinkThreshold, errorBody);

        // Mark delivery failed.
        await MarkDeliveryFailedAsync(
            notification,
            $"Chat unreachable ({failureCount}/{dirtyLinkThreshold}): {errorBody}",
            ct);

        // ── Check threshold and auto-disable (REQ-NOTIFY-019) ───────────────
        if (failureCount >= dirtyLinkThreshold)
        {
            _logger.LogWarning(
                "NDJ: user {UserId} Telegram link marked as dirty after " +
                "{FailureCount} consecutive failures (threshold: {Threshold}). " +
                "REQ-NOTIFY-019: auto-disabling Telegram link.",
                user.Id, failureCount, dirtyLinkThreshold);

            var dirtyUpdate = Builders<UserDocument>.Update
                .Set(u => u.TelegramIsDirty, true);
            await usersCollection.UpdateOneAsync(
                Builders<UserDocument>.Filter.Eq(u => u.Id, user.Id),
                dirtyUpdate,
                cancellationToken: ct);
        }
    }

    /// <summary>
    /// Marks a notification as successfully delivered (REQ-NOTIFY-023).
    /// Persists telegram_message_id as the durable acknowledgement.
    /// Resets the user's consecutive failure counter.
    /// </summary>
    private async Task MarkDeliverySucceededAsync(
        NotificationDocument notification,
        string? messageId,
        UserDocument user,
        CancellationToken ct)
    {
        var notificationsCollection = _database
            .GetCollection<NotificationDocument>("notifications");

        // Update fields on the notification record.
        var filter = Builders<NotificationDocument>.Filter.Eq(n => n.Id, notification.Id);
        var update = Builders<NotificationDocument>.Update
            .Set(n => n.TelegramDeliveryStatus, TelegramDeliveryDelivered)
            .Set(n => n.TelegramDeliveryAttempts, notification.TelegramDeliveryAttempts + 1)
            .Set(n => n.LastTelegramAttemptAt, DateTime.UtcNow);

        if (messageId is not null)
        {
            update = update.Set(n => n.TelegramMessageId, messageId);
        }

        await notificationsCollection.UpdateOneAsync(filter, update, cancellationToken: ct);

        // Reset consecutive failure counter on the user document.
        if (user.TelegramConsecutiveFailures > 0)
        {
            var usersCollection = _database.GetCollection<UserDocument>("users");
            var resetUpdate = Builders<UserDocument>.Update
                .Set(u => u.TelegramConsecutiveFailures, 0);
            await usersCollection.UpdateOneAsync(
                Builders<UserDocument>.Filter.Eq(u => u.Id, user.Id),
                resetUpdate,
                cancellationToken: ct);
        }
    }

    /// <summary>
    /// Marks a notification as delivery failed and increments the attempt counter.
    /// </summary>
    private async Task MarkDeliveryFailedAsync(
        NotificationDocument notification,
        string errorDetail,
        CancellationToken ct)
    {
        var collection = _database.GetCollection<NotificationDocument>("notifications");

        var filter = Builders<NotificationDocument>.Filter.Eq(n => n.Id, notification.Id);
        var update = Builders<NotificationDocument>.Update
            .Set(n => n.TelegramDeliveryStatus, TelegramDeliveryFailed)
            .Set(n => n.TelegramDeliveryAttempts, notification.TelegramDeliveryAttempts + 1)
            .Set(n => n.LastTelegramAttemptAt, DateTime.UtcNow)
            .Set(n => n.ErrorDetails, errorDetail);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <summary>
    /// Increments the delivery attempt counter without marking as failed.
    /// Used for transient errors that may be retried.
    /// </summary>
    private async Task IncrementDeliveryAttemptAsync(
        NotificationDocument notification,
        string errorDetail,
        CancellationToken ct)
    {
        var collection = _database.GetCollection<NotificationDocument>("notifications");

        var filter = Builders<NotificationDocument>.Filter.Eq(n => n.Id, notification.Id);
        var update = Builders<NotificationDocument>.Update
            .Inc(n => n.TelegramDeliveryAttempts, 1)
            .Set(n => n.LastTelegramAttemptAt, DateTime.UtcNow)
            .Set(n => n.ErrorDetails, errorDetail);

        await collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    /// <summary>
    /// Extracts the message_id from a successful Telegram sendMessage response.
    /// Returns null if parsing fails.
    /// </summary>
    private static string? ExtractMessageId(string responseBody)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
            {
                if (doc.RootElement.TryGetProperty("result", out var result)
                    && result.TryGetProperty("message_id", out var messageId))
                {
                    return messageId.GetInt64().ToString();
                }
            }
        }
        catch (JsonException)
        {
            // Ignore parse errors.
        }

        return null;
    }
}
