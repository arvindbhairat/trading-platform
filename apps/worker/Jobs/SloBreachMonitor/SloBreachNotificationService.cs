using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using SignalStack.Domain.Notifications;
using SignalStack.Storage.Notifications;
using SignalStack.Notifications;
using SignalStack.Notifications.TelegramBot;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Jobs.SloBreachMonitor;

/// <summary>
/// Outcome of reporting an SLO breach.
/// </summary>
public enum SloBreachResult
{
    /// <summary>A notification was fired for this breach.</summary>
    Notified,
    /// <summary>The breach was suppressed by the in-cooldown or same-day gate.</summary>
    Suppressed,
}

/// <summary>
/// Routes SLO breach events to admin notification channels with per-SLO
/// cooldown suppression and metric emission.
///
/// REQ-SLO-008: On first SLO breach per calendar day, admin Telegram + email
/// notification fires naming the breached SLO, observed p95, target, and
/// detection time. Cooldown applies per SLO per session.
/// </summary>
public sealed class SloBreachNotificationService
{
    private static readonly Meter Meter = new(WorkerTelemetry.ServiceName);

    internal static readonly Counter<long> BreachNotificationFiredCounter = Meter.CreateCounter<long>(
        "slo.breach_notification_fired",
        description: "Count of SLO breach notifications fired (not suppressed by cooldown).");

    /// <summary>Per-SLO last-notification-fire-time (UTC).</summary>
    internal readonly ConcurrentDictionary<string, DateTime> LastFiredPerSlo = new(StringComparer.Ordinal);

    private readonly INotificationWriter _notificationWriter;
    private readonly IOptions<SloBreachMonitorOptions> _options;
    private readonly ILogger<SloBreachNotificationService> _logger;

    public SloBreachNotificationService(
        INotificationWriter notificationWriter,
        IOptions<SloBreachMonitorOptions> options,
        ILogger<SloBreachNotificationService> logger)
    {
        _notificationWriter = notificationWriter ?? throw new ArgumentNullException(nameof(notificationWriter));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reports an SLO breach. Fires an admin notification if the cooldown
    /// gate allows it; otherwise suppresses the breach.
    /// </summary>
    /// <param name="sloName">Name of the breached SLO (e.g. "DataSync p95 latency").</param>
    /// <param name="observedP95">Observed p95 value in milliseconds.</param>
    /// <param name="target">SLO target value in milliseconds.</param>
    /// <param name="adminUserId">ObjectId of the admin user to notify.</param>
    /// <param name="detectionTime">UTC timestamp when the breach was detected.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><see cref="SloBreachResult.Notified"/> if a notification was fired;
    /// <see cref="SloBreachResult.Suppressed"/> if the cooldown gate suppressed it.</returns>
    public async Task<SloBreachResult> ReportBreachAsync(
        string sloName,
        double observedP95,
        double target,
        ObjectId adminUserId,
        DateTime detectionTime,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sloName);

        // ── Step 1: Cooldown gate ──────────────────────────────────────────
        // First breach per calendar day fires immediately; subsequent breaches
        // within the cooldown window are suppressed.
        var now = DateTime.UtcNow;
        var cooldownMinutes = _options.Value.BreachNotificationCooldownMinutes;
        var todayStart = now.Date;

        if (LastFiredPerSlo.TryGetValue(sloName, out var lastFired))
        {
            // Re-arm if the last fire was on a previous calendar day.
            if (lastFired >= todayStart)
            {
                var elapsedMinutes = (now - lastFired).TotalMinutes;
                if (elapsedMinutes < cooldownMinutes)
                {
                    _logger.LogInformation(
                        "SLO breach suppressed for '{SloName}': last notified at {LastFired:O}, " +
                        "cooldown {Cooldown}m, elapsed {Elapsed:F1}m.",
                        sloName, lastFired, cooldownMinutes, elapsedMinutes);

                    return SloBreachResult.Suppressed;
                }

                _logger.LogInformation(
                    "SLO breach re-notifying for '{SloName}': cooldown {Cooldown}m elapsed " +
                    "({Elapsed:F1}m since last fire).",
                    sloName, cooldownMinutes, elapsedMinutes);
            }
        }

        // ── Step 2: Build and write the notification ───────────────────────
        var content = NotificationTemplateBuilder.BuildSloBreachContent(
            sloName, observedP95, target, detectionTime);

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = adminUserId,
            NotificationType = NotificationType.AdminSloBreach,
            Content = content,
            GeneratedAt = now,
            IsAdminNotification = true,
        };

        await _notificationWriter.WriteAsync(notification, ct);

        // ── Step 3: Update cooldown state ──────────────────────────────────
        LastFiredPerSlo[sloName] = now;

        // ── Step 4: Emit metric ────────────────────────────────────────────
        BreachNotificationFiredCounter.Add(1,
            new KeyValuePair<string, object?>("slo.name", sloName),
            new KeyValuePair<string, object?>("slo.breach.observed_p95_ms", observedP95),
            new KeyValuePair<string, object?>("slo.breach.target_ms", target));

        _logger.LogInformation(
            "SLO breach notification fired for '{SloName}': p95={ObservedP95:F1}ms, " +
            "target={Target:F0}ms, detected at {DetectionTime:O}.",
            sloName, observedP95, target, detectionTime);

        return SloBreachResult.Notified;
    }

    /// <summary>
    /// Returns the current cooldown state for diagnostics.
    /// </summary>
    internal IReadOnlyDictionary<string, DateTime> GetCooldownState() =>
        new Dictionary<string, DateTime>(LastFiredPerSlo);
}
