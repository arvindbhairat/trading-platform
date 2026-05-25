using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Notifications;
using SignalStack.Storage.Notifications;
using SignalStack.Notifications.TelegramBot;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Detects material divergence between FYERS total account value and
/// platform-visible equity, writing an advisory notification when the
/// gap exceeds the configured threshold (REQ-RME-006e).
///
/// The divergence ratio is: |FYERS_total − platform_visible| / platform_visible,
/// expressed as a percentage. Threshold defaults to 15% via the sys_config
/// key <c>risk.equity_base.external_divergence_warn_pct</c>.
/// </summary>
internal sealed class EquityBaseDivergenceService : IEquityBaseDivergenceService
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<EquityBaseDivergenceService> _logger;

    private const decimal DefaultWarnThresholdPct = 15m;
    private const string SysConfigKey = "risk.equity_base.external_divergence_warn_pct";
    private const string NotificationCollection = "notifications";

    public EquityBaseDivergenceService(
        IMongoDatabase database,
        ILogger<EquityBaseDivergenceService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<DivergenceAssessment> AssessDivergenceAsync(
        string userId,
        EquityBaseResult equityResult,
        CancellationToken ct = default)
    {
        // Read threshold from sys_config (cached per call).
        var threshold = await ReadDivergenceWarnThresholdPctAsync(ct);

        // Both values must be available to compute divergence.
        if (equityResult.FyersTotalAccountValue is null
            || equityResult.PlatformVisibleEquity is null
            || equityResult.PlatformVisibleEquity <= 0)
        {
            return new DivergenceAssessment(
                HasDivergence: false,
                FyersTotal: equityResult.FyersTotalAccountValue,
                PlatformVisible: equityResult.PlatformVisibleEquity,
                GapPercent: null,
                WarnThresholdPct: threshold,
                Action: DivergenceAction.None);
        }

        var fyersTotal = equityResult.FyersTotalAccountValue.Value;
        var platformVisible = equityResult.PlatformVisibleEquity.Value;

        // Compute absolute divergence ratio as a percentage.
        var gap = Math.Abs(fyersTotal - platformVisible);
        var gapPct = gap / platformVisible * 100m;

        // Round to two decimal places for readability.
        gapPct = Math.Round(gapPct, 2);

        if (gapPct <= threshold)
        {
            _logger.LogTrace(
                "Divergence assessment for user {UserId}: gap {GapPct}% is within threshold {Threshold}%.",
                userId, gapPct, threshold);

            return new DivergenceAssessment(
                HasDivergence: false,
                FyersTotal: fyersTotal,
                PlatformVisible: platformVisible,
                GapPercent: gapPct,
                WarnThresholdPct: threshold,
                Action: DivergenceAction.None);
        }

        // Threshold breached — write advisory notification.
        _logger.LogInformation(
            "Equity-base divergence detected for user {UserId}: FYERS total ₹{FyersTotal:N0}, " +
            "platform-visible ₹{PlatformVisible:N0}, gap {GapPct}% exceeds threshold {Threshold}%. " +
            "Writing equity_base_divergence_advisory notification.",
            userId, fyersTotal, platformVisible, gapPct, threshold);

        await WriteDivergenceNotificationAsync(
            userId, fyersTotal, platformVisible, gapPct, ct);

        return new DivergenceAssessment(
            HasDivergence: true,
            FyersTotal: fyersTotal,
            PlatformVisible: platformVisible,
            GapPercent: gapPct,
            WarnThresholdPct: threshold,
            Action: DivergenceAction.AdvisoryWritten);
    }

    /// <summary>
    /// Writes an <c>equity_base_divergence_advisory</c> notification document
    /// to the MongoDB notifications collection.
    /// </summary>
    private async Task WriteDivergenceNotificationAsync(
        string userId,
        decimal fyersTotal,
        decimal platformVisible,
        decimal gapPct,
        CancellationToken ct)
    {
        // Parse the user ID as an ObjectId for the notification document.
        if (!ObjectId.TryParse(userId, out var userOid))
        {
            _logger.LogWarning(
                "Cannot write divergence notification: user ID '{UserId}' is not a valid ObjectId.",
                userId);
            return;
        }

        var notifications = _database.GetCollection<BsonDocument>(NotificationCollection);

        var notification = new BsonDocument
        {
            ["user_id"] = userOid,
            ["notification_type"] = NotificationType.EquityBaseDivergenceAdvisory,
            ["is_admin_notification"] = false,
            ["content"] =
                $"Your FYERS total account value (₹{fyersTotal:N0}) differs from the " +
                $"platform-visible equity base (₹{platformVisible:N0}) by {gapPct}%. " +
                $"Holdings outside the Nifty 500 universe are excluded from RME calculations. " +
                $"You may set a manual equity override in your profile if you want the risk engine " +
                $"to size against your full account value.",
            ["deep_link"] = "/profile/settings",
            ["generated_at"] = DateTime.UtcNow,
            ["is_read"] = false,
        };

        await notifications.InsertOneAsync(notification, cancellationToken: ct);

        _logger.LogDebug(
            "Divergence advisory notification written for user {UserId}.",
            userId);
    }

    /// <summary>
    /// Reads the divergence warn threshold from sys_config.
    /// Falls back to 15% if the key is missing or unparseable.
    /// </summary>
    private async Task<decimal> ReadDivergenceWarnThresholdPctAsync(CancellationToken ct)
    {
        try
        {
            var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
            var filter = Builders<BsonDocument>.Filter.Eq("key", SysConfigKey);
            var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

            if (doc is null)
                return DefaultWarnThresholdPct;

            var value = doc.GetValue("value", BsonNull.Value);
            if (!value.IsBsonNull
                && decimal.TryParse(value.AsString,
                    System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to read {Key} from sys_config. Using default {Default}%.",
                SysConfigKey, DefaultWarnThresholdPct);
        }

        return DefaultWarnThresholdPct;
    }
}
