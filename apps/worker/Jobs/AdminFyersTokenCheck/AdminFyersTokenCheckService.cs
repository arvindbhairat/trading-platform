using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Admin;
using SignalStack.Api.Notifications;

namespace SignalStack.Worker.Jobs.AdminFyersTokenCheck;

/// <summary>
/// Core service for the Admin FYERS Token Validity Check job.
///
/// Runs two daily checks on NSE trading days:
///   1. Pre-market check (~08:30 IST) — token valid through full market session.
///   2. Daily check (~15:00 IST) — token valid through DataSync window.
///
/// If the admin FYERS token is absent, expired, or will not remain valid
/// through the required window, generates an
/// <c>admin_fyers_token_expiry_warning</c> notification delivered via
/// Telegram and email unconditionally.
///
/// At most one alert fires per NSE trading session across both checks.
///
/// REQ-NOTIFY-022:  daily 15:00 IST DataSync-window check.
/// REQ-NOTIFY-022b: pre-market 08:30 IST check.
/// </summary>
public sealed class AdminFyersTokenCheckService
{
    private readonly IMongoDatabase _database;
    private readonly IOptions<AdminFyersTokenCheckOptions> _options;
    private readonly ILogger<AdminFyersTokenCheckService> _logger;

    // ── Per-session tracking state ────────────────────────────────────────
    // REQ-NOTIFY-022: at-most-one alert per NSE trading session.
    // Key: DateOnly.ToString("yyyy-MM-dd") of the trading session date.
    private static readonly HashSet<string> AlertFiredSessions = new(StringComparer.Ordinal);
    private static readonly object AlertLock = new();

    // Tracks whether each check has been completed for the current tracking date.
    private DateOnly _currentTrackingDate;
    private bool _preMarketCheckDone;
    private bool _dailyCheckDone;
    private readonly object _trackingLock = new();

    public AdminFyersTokenCheckService(
        IMongoDatabase database,
        IOptions<AdminFyersTokenCheckOptions> options,
        ILogger<AdminFyersTokenCheckService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Attempts to run the admin FYERS token checks for today.
    /// This method is called periodically by the worker; it returns immediately
    /// if no check is due at the current time.
    /// </summary>
    /// <param name="calendar">Trading calendar repository, resolved by the caller.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task TryRunChecksAsync(
        ITradingCalendarRepository calendar,
        CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // ── Step 1: Is today a trading day? ──────────────────────────────────
        var sessions = await calendar.GetSessionsAsync(
            fromDate: today.ToString("yyyy-MM-dd"),
            toDate: today.ToString("yyyy-MM-dd"),
            ct: ct);

        if (sessions.Count == 0)
        {
            _logger.LogTrace("Today {Date} is not a trading day. Skipping admin token check.", today);
            ResetTracking();
            return;
        }

        // ── Step 2: Reset tracking on date boundary ──────────────────────────
        ResetTrackingIfNewDay(today);

        // ── Step 3: Get current time in IST ──────────────────────────────────
        var nowIst = GetCurrentIstTime();
        if (nowIst is null)
        {
            _logger.LogWarning("Could not resolve IST timezone. Skipping token check.");
            return;
        }

        // ── Step 4: Read configured check times from sys_config ──────────────
        var preMarketTimeStr = await ReadSysConfigStringAsync(
            "jobs.admin_token_check.pre_market_check_time", ct);
        var dailyCheckTimeStr = await ReadSysConfigStringAsync(
            "jobs.admin_token_check.daily_check_time", ct);

        var preMarketTime = TimeOnly.TryParse(
            preMarketTimeStr ?? _options.Value.DefaultPreMarketCheckTime, out var pmt) ? pmt : TimeOnly.Parse("08:30");
        var dailyCheckTime = TimeOnly.TryParse(
            dailyCheckTimeStr ?? _options.Value.DefaultDailyCheckTime, out var dct) ? dct : TimeOnly.Parse("15:00");

        // ── Step 5: Determine which checks are due ───────────────────────────
        var preMarketDue = !_preMarketCheckDone
            && nowIst.Value.Hour == preMarketTime.Hour
            && nowIst.Value.Minute == preMarketTime.Minute;

        var dailyDue = !_dailyCheckDone
            && nowIst.Value.Hour == dailyCheckTime.Hour
            && nowIst.Value.Minute == dailyCheckTime.Minute;

        if (!preMarketDue && !dailyDue)
        {
            _logger.LogTrace(
                "Admin token check: no check due at {Time:hh\\:mm} IST.", nowIst.Value);
            return;
        }

        // ── Step 6: Execute due checks ───────────────────────────────────────
        if (preMarketDue)
        {
            await ExecutePreMarketCheckAsync(nowIst.Value, ct);
            MarkCheckDone(today, preMarketDone: true);
        }

        if (dailyDue)
        {
            await ExecuteDailyCheckAsync(nowIst.Value, ct);
            MarkCheckDone(today, dailyDone: true);
        }
    }

    /// <summary>
    /// Pre-market check: validates the admin token covers the full market
    /// session (09:15–15:30 IST). REQ-NOTIFY-022b.
    /// </summary>
    private async Task ExecutePreMarketCheckAsync(
        TimeOnly nowIst, CancellationToken ct)
    {
        _logger.LogInformation("Admin token pre-market check triggered at {Time:hh\\:mm} IST.", nowIst);

        var sessionEndStr = _options.Value.MarketSessionEndTime;
        var sessionEnd = TimeOnly.TryParse(sessionEndStr, out var se) ? se : new TimeOnly(15, 30);

        var (isValid, tokenInfo) = await EvaluateTokenValidityAsync(sessionEnd, ct);
        await HandleCheckOutcomeAsync(isValid, tokenInfo, "pre-market", ct);
    }

    /// <summary>
    /// Daily check: validates the admin token covers the DataSync window
    /// after market close. REQ-NOTIFY-022.
    /// </summary>
    private async Task ExecuteDailyCheckAsync(
        TimeOnly nowIst, CancellationToken ct)
    {
        _logger.LogInformation("Admin token daily check triggered at {Time:hh\\:mm} IST.", nowIst);

        // Daily check covers the DataSync window — treat end-of-day as the
        // relevant boundary (same as market session end by default).
        var windowEndStr = _options.Value.MarketSessionEndTime;
        var windowEnd = TimeOnly.TryParse(windowEndStr, out var we) ? we : new TimeOnly(15, 30);

        var (isValid, tokenInfo) = await EvaluateTokenValidityAsync(windowEnd, ct);
        await HandleCheckOutcomeAsync(isValid, tokenInfo, "daily", ct);
    }

    /// <summary>
    /// Evaluates whether the admin FYERS token is present, active, and will
    /// remain valid through the required IST end time.
    /// Returns a tuple of (isValid, tokenInfo) where tokenInfo is a
    /// human-readable description used in the notification content.
    /// </summary>
    private async Task<(bool IsValid, string TokenInfo)> EvaluateTokenValidityAsync(
        TimeOnly requiredEndTimeIst, CancellationToken ct)
    {
        var fyersTokens = _database.GetCollection<BsonDocument>("fyers_tokens");

        var adminTokenFilter = Builders<BsonDocument>.Filter.And(
            Builders<BsonDocument>.Filter.Eq("user_id", "admin"),
            Builders<BsonDocument>.Filter.Eq("status", "active"));

        var token = await fyersTokens.Find(adminTokenFilter)
            .Sort(Builders<BsonDocument>.Sort.Descending("issued_at"))
            .FirstOrDefaultAsync(ct);

        if (token is null)
        {
            return (false, "No admin FYERS token is currently on record.");
        }

        var expiresAt = token.TryGetValue("expires_at", out var expiresValue)
            ? expiresValue.ToUniversalTime() : (DateTime?)null;

        var issuedAt = token.TryGetValue("issued_at", out var issuedValue)
            ? issuedValue.ToUniversalTime() : (DateTime?)null;

        var tokenStatus = token.GetValue("status", "unknown").AsString;

        // Build the required-end UTC time from today's IST end time.
        var todayIst = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(DateTime.UtcNow, GetIstTimeZone()));
        var requiredEndUtc = todayIst.ToDateTime(requiredEndTimeIst, DateTimeKind.Unspecified)
            .AddHours(-5).AddMinutes(-30);

        if (expiresAt is null || expiresAt < requiredEndUtc)
        {
            var expiryStr = expiresAt?.ToString("yyyy-MM-dd HH:mm:ss UTC") ?? "unknown";
            return (false,
                $"Admin FYERS token status: {tokenStatus}. " +
                $"Expires at: {expiryStr}. " +
                $"Token will not remain valid through the required window " +
                $"(end: {requiredEndTimeIst:hh\\:mm} IST).");
        }

        return (true,
            $"Admin FYERS token is active and valid. " +
            $"Expires at: {expiresAt.Value:yyyy-MM-dd HH:mm:ss UTC}.");
    }

    /// <summary>
    /// Handles the check outcome: fires a notification if the token is invalid
    /// and no alert has been fired for this session yet.
    /// REQ-NOTIFY-022: at-most-one alert per trading session.
    /// Notification content includes token expiry, DataSync time context,
    /// and a link to the admin token renewal page.
    /// </summary>
    private async Task HandleCheckOutcomeAsync(
        bool isValid, string tokenInfo, string checkType, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sessionKey = today.ToString("yyyy-MM-dd");

        if (isValid)
        {
            _logger.LogInformation(
                "Admin token {CheckType} check PASSED. {TokenInfo}",
                checkType, tokenInfo);
            return;
        }

        // ── At-most-one alert per session (REQ-NOTIFY-022) ───────────────────
        lock (AlertLock)
        {
            if (AlertFiredSessions.Contains(sessionKey))
            {
                _logger.LogInformation(
                    "Admin token {CheckType} check FAILED but alert already " +
                    "fired for session {Session}. Skipping notification.",
                    checkType, sessionKey);
                return;
            }

            AlertFiredSessions.Add(sessionKey);
        }

        // Read the scheduled DataSync run time for context in notification.
        var dailyCheckTime = await ReadSysConfigStringAsync(
            "jobs.admin_token_check.daily_check_time", ct) ?? _options.Value.DefaultDailyCheckTime;

        _logger.LogWarning(
            "Admin token {CheckType} check FAILED. Firing " +
            "admin_fyers_token_expiry_warning notification. {TokenInfo}",
            checkType, tokenInfo);

        // ── Write the notification (REQ-NOTIFY-022 / REQ-NOTIFY-022b) ────────
        // REQ-NOTIFY-022: content must include expiry time (or no-token statement),
        // scheduled DataSync run time, and direct link to admin token renewal page.
        var notifications = _database.GetCollection<BsonDocument>("notifications");

        var content = $"Admin FYERS token check ({checkType}, " +
            $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC): " +
            $"TOKEN VALIDATION FAILED. {tokenInfo} " +
            $"Scheduled DataSync window: {dailyCheckTime} IST. " +
            $"Renew the admin FYERS token immediately via the admin portal " +
            $"at Settings → FYERS Token Management.";

        var notification = new BsonDocument
        {
            ["notification_type"] = NotificationType.AdminFyersTokenExpiryWarning,
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
            "Admin FYERS token expiry warning notification written " +
            "for session {Session} ({CheckType} check).",
            sessionKey, checkType);
    }

    /// <summary>
    /// Reads a string value from sys_config by key.
    /// </summary>
    private async Task<string?> ReadSysConfigStringAsync(
        string key, CancellationToken ct)
    {
        var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
        var filter = Builders<BsonDocument>.Filter.Eq("key", key);
        var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

        if (doc is null)
            return null;

        var value = doc.GetValue("value", BsonNull.Value);
        return value.IsBsonNull ? null : value.AsString;
    }

    /// <summary>
    /// Returns the current time in IST (UTC+5:30), or null if the timezone
    /// cannot be resolved.
    /// </summary>
    private static TimeOnly? GetCurrentIstTime()
    {
        try
        {
            var istZone = GetIstTimeZone();
            var istNow = TimeZoneInfo.ConvertTime(DateTime.UtcNow, istZone);
            return TimeOnly.FromDateTime(istNow);
        }
        catch
        {
            return null;
        }
    }

    private static TimeZoneInfo GetIstTimeZone()
    {
        // "India Standard Time" works on Windows; "Asia/Kolkata" on Linux/macOS.
        return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time")
            ?? TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
    }

    /// <summary>
    /// Resets check-completed tracking when the date changes.
    /// </summary>
    private void ResetTrackingIfNewDay(DateOnly today)
    {
        lock (_trackingLock)
        {
            if (_currentTrackingDate != today)
            {
                _currentTrackingDate = today;
                _preMarketCheckDone = false;
                _dailyCheckDone = false;
            }
        }
    }

    /// <summary>
    /// Marks one or both checks as completed for the current tracking date.
    /// </summary>
    private void MarkCheckDone(DateOnly today, bool preMarketDone = false, bool dailyDone = false)
    {
        lock (_trackingLock)
        {
            _currentTrackingDate = today;
            if (preMarketDone) _preMarketCheckDone = true;
            if (dailyDone) _dailyCheckDone = true;
        }
    }

    /// <summary>
    /// Resets all tracking state (used when skipping a non-trading day).
    /// </summary>
    private void ResetTracking()
    {
        lock (_trackingLock)
        {
            _currentTrackingDate = default;
            _preMarketCheckDone = false;
            _dailyCheckDone = false;
        }
    }
}
