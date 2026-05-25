using MongoDB.Bson;
using SignalStack.Api.Notifications;
using SignalStack.Domain.Notifications;
using SignalStack.Storage.Notifications;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Tests for notification writer paths from EODSR, LMDS, and RME producers.
/// REQ-NOTIFY-006: all notification types are accepted by the schema.
/// REQ-NOTIFY-007: producers write to notifications collection; no Telegram dispatch here.
/// </summary>
public sealed class NotificationWriterTests
{
    private readonly InMemoryNotificationRepository _repository;
    private readonly NotificationWriter _writer;

    public NotificationWriterTests()
    {
        _repository = new InMemoryNotificationRepository();
        _writer = new NotificationWriter(_repository);
    }

    // ── EODSR Producer Path ────────────────────────────────────────────────
    // EOD Signal Runner generates entry_signal notifications when entry signals
    // are emitted for subscribed Signal types.

    [Fact]
    public async Task Eodsr_writes_entry_signal_notification()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.EntrySignal,
            SignalTypeName = "MA Crossover",
            Timeframe = "daily",
            Symbol = "RELIANCE",
            Content = "Your MA Crossover scan matched RELIANCE on daily timeframe with an entry price of ₹2,450. This is not investment advice.",
            DeepLink = "/chart/RELIANCE?signal=ma_crossover",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.EntrySignal, all[0].NotificationType);
        Assert.Equal(userId, all[0].UserId);
        Assert.Equal("RELIANCE", all[0].Symbol);
        Assert.Equal("MA Crossover", all[0].SignalTypeName);
    }

    [Fact]
    public async Task Eodsr_writes_batch_of_entry_signals()
    {
        var userId = ObjectId.GenerateNewId();
        var symbols = new[] { "HDFCBANK", "TCS", "INFY", "ICICIBANK", "SBIN" };

        var notifications = symbols.Select(s => new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.EntrySignal,
            SignalTypeName = "Volume Spike",
            Timeframe = "daily",
            Symbol = s,
            Content = $"Your Volume Spike scan matched {s} on daily timeframe. This is not investment advice.",
            DeepLink = $"/chart/{s}?signal=volume_spike",
            GeneratedAt = DateTime.UtcNow,
        }).ToList();

        await _writer.WriteBatchAsync(notifications);

        var all = _repository.GetAll();
        Assert.Equal(5, all.Count);
        Assert.All(all, n => Assert.Equal(NotificationType.EntrySignal, n.NotificationType));
        Assert.All(all, n => Assert.Equal(userId, n.UserId));
    }

    // ── LMDS Producer Path ─────────────────────────────────────────────────
    // Live Market Data Scan generates exit alerts, gap risk alerts, circuit
    // limit alerts, and market halt notifications during intraday monitoring.

    [Fact]
    public async Task Lmds_writes_exit_alert_notification()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.ExitAlert,
            Symbol = "TATAMOTORS",
            Content = "Your configured stop level was triggered for TATAMOTORS at ₹620. This is not investment advice.",
            DeepLink = "/chart/TATAMOTORS",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.ExitAlert, all[0].NotificationType);
    }

    [Fact]
    public async Task Lmds_writes_gap_risk_alert()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.GapRiskAlert,
            Symbol = "MARUTI",
            Content = "MARUTI opened at ₹9,800, gapping past your stop level of ₹10,200. This is not investment advice.",
            DeepLink = "/chart/MARUTI",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.GapRiskAlert, all[0].NotificationType);
    }

    [Fact]
    public async Task Lmds_writes_circuit_limit_alert()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.CircuitLimitAlert,
            Symbol = "IDEA",
            Content = "IDEA hit the lower circuit at ₹8.50. Monitor for recovery or further downside. This is not investment advice.",
            DeepLink = "/chart/IDEA",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.CircuitLimitAlert, all[0].NotificationType);
    }

    [Fact]
    public async Task Lmds_writes_market_halt_notifications()
    {
        var userId = ObjectId.GenerateNewId();

        var active = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.MarketHaltActive,
            Content = "NSE 10% circuit triggered at 10:45 IST. Market halt is active. No stop, add, or reduce advisories will fire during the halt. This is not investment advice.",
            GeneratedAt = DateTime.UtcNow,
        };

        var cleared = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.MarketHaltCleared,
            Content = "NSE market halt cleared at 11:30 IST. Normal monitoring has resumed. This is not investment advice.",
            GeneratedAt = DateTime.UtcNow.AddMinutes(45),
        };

        await _writer.WriteBatchAsync(new[] { active, cleared });

        var all = _repository.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Contains(all, n => n.NotificationType == NotificationType.MarketHaltActive);
        Assert.Contains(all, n => n.NotificationType == NotificationType.MarketHaltCleared);
    }

    // ── RME Producer Path ───────────────────────────────────────────────────
    // Risk Management Engine generates portfolio impact, drawdown, heat
    // warnings, and pending-entry lifecycle notifications.

    [Fact]
    public async Task Rme_writes_portfolio_impact_insight()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.PortfolioImpactInsight,
            Content = "RELIANCE has reached your configured add level of ₹2,500. Consider reviewing your position. This is not investment advice.",
            Symbol = "RELIANCE",
            DeepLink = "/chart/RELIANCE",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.PortfolioImpactInsight, all[0].NotificationType);
    }

    [Fact]
    public async Task Rme_writes_drawdown_mode_alert()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.DrawdownModeAlert,
            Content = "Your portfolio drawdown has reached 6.5% (warning threshold: 5%). Sizing for new entries has been reduced. This is not investment advice.",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.DrawdownModeAlert, all[0].NotificationType);
    }

    [Fact]
    public async Task Rme_writes_portfolio_heat_warning()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.PortfolioHeatWarning,
            Content = "Your portfolio heat is at 4.2% (warning threshold: 4%). New entries may increase exposure beyond your configured limits. This is not investment advice.",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.PortfolioHeatWarning, all[0].NotificationType);
    }

    [Fact]
    public async Task Rme_writes_pending_entry_superseded()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.PendingEntrySuperseded,
            Symbol = "HDFCBANK",
            Content = "Your pending entry for HDFCBANK has been updated with a fresh signal from the latest EOD scan. This is not investment advice.",
            DeepLink = "/chart/HDFCBANK",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.PendingEntrySuperseded, all[0].NotificationType);
    }

    [Fact]
    public async Task Rme_writes_pending_entry_expired()
    {
        var userId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.PendingEntryExpired,
            Symbol = "WIPRO",
            Content = "Your pending entry for WIPRO has expired without fill confirmation. No action is needed. This is not investment advice.",
            DeepLink = "/chart/WIPRO",
            GeneratedAt = DateTime.UtcNow,
        };

        await _writer.WriteAsync(notification);

        var all = _repository.GetAll();
        Assert.Single(all);
        Assert.Equal(NotificationType.PendingEntryExpired, all[0].NotificationType);
    }

    // ── Schema Validation ──────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_invalid_notification_type()
    {
        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = ObjectId.GenerateNewId(),
            NotificationType = "invalid_type_that_does_not_exist",
            Content = "This should be rejected.",
            GeneratedAt = DateTime.UtcNow,
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _writer.WriteAsync(notification));
        Assert.Contains("invalid_type_that_does_not_exist", ex.Message);
        Assert.Empty(_repository.GetAll());
    }

    [Fact]
    public async Task Rejects_invalid_type_in_batch()
    {
        var userId = ObjectId.GenerateNewId();
        var valid = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.EntrySignal,
            Content = "Valid notification.",
            GeneratedAt = DateTime.UtcNow,
        };
        var invalid = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = "bogus_type",
            Content = "Invalid notification.",
            GeneratedAt = DateTime.UtcNow,
        };

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => _writer.WriteBatchAsync(new[] { valid, invalid }));
        Assert.Contains("bogus_type", ex.Message);
        Assert.Empty(_repository.GetAll());
    }

    // ── Admin Notification Routing (REQ-NOTIFY-006a) ───────────────────────

    [Fact]
    public async Task Admin_notification_is_excluded_from_user_feed()
    {
        var userId = ObjectId.GenerateNewId();
        var adminUserId = ObjectId.GenerateNewId();

        var userNotif = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = userId,
            NotificationType = NotificationType.EntrySignal,
            Content = "User notification.",
            GeneratedAt = DateTime.UtcNow,
        };
        var adminNotif = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = adminUserId,
            NotificationType = NotificationType.AdminDataSyncJobFailure,
            Content = "DataSync job failed.",
            GeneratedAt = DateTime.UtcNow,
            IsAdminNotification = true,
        };

        await _writer.WriteBatchAsync(new[] { userNotif, adminNotif });

        var userNotifs = await _repository.GetByUserIdAsync(userId);
        var adminNotifs = await _repository.GetAdminNotificationsAsync();

        Assert.Single(userNotifs);
        Assert.Equal(NotificationType.EntrySignal, userNotifs[0].NotificationType);

        Assert.Single(adminNotifs);
        Assert.Equal(NotificationType.AdminDataSyncJobFailure, adminNotifs[0].NotificationType);
    }

    // ── All Admin-Only Types Accepted by Schema (REQ-NOTIFY-006a) ─────────

    [Fact]
    public async Task All_admin_notification_types_are_accepted_by_schema()
    {
        var userId = ObjectId.GenerateNewId();

        foreach (var type in NotificationType.AdminOnly)
        {
            var notification = new NotificationDocument
            {
                Id = ObjectId.GenerateNewId(),
                UserId = userId,
                NotificationType = type,
                Content = $"Test admin notification for {type}.",
                GeneratedAt = DateTime.UtcNow,
                IsAdminNotification = true,
            };
            await _writer.WriteAsync(notification);
        }

        var all = _repository.GetAll();
        Assert.Equal(NotificationType.AdminOnly.Count, all.Count);

        var writtenTypes = all.Select(n => n.NotificationType).ToHashSet();
        Assert.True(NotificationType.AdminOnly.SetEquals(writtenTypes));
    }

    // ── Admin SLO Breach Notification (REQ-SLO-008) ──────────────────────

    [Fact]
    public async Task Slo_breach_admin_notification_is_accepted()
    {
        var adminUserId = ObjectId.GenerateNewId();

        var notification = new NotificationDocument
        {
            Id = ObjectId.GenerateNewId(),
            UserId = adminUserId,
            NotificationType = NotificationType.AdminSloBreach,
            Content = "Test SLO breach notification.",
            GeneratedAt = DateTime.UtcNow,
            IsAdminNotification = true,
        };

        await _writer.WriteAsync(notification);

        var adminNotifs = await _repository.GetAdminNotificationsAsync();
        var match = Assert.Single(adminNotifs);
        Assert.Equal(NotificationType.AdminSloBreach, match.NotificationType);
    }
}
