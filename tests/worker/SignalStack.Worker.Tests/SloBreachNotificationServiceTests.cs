using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using SignalStack.Domain.Notifications;
using SignalStack.Storage.Notifications;
using SignalStack.Worker.Jobs.SloBreachMonitor;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests for SLO breach notification routing with cooldown suppression.
///
/// REQ-SLO-008: On first SLO breach per calendar day, admin Telegram + email
/// notification fires naming the breached SLO, observed p95, target, and
/// detection time. Cooldown applies per SLO per session.
/// </summary>
public sealed class SloBreachNotificationServiceTests
{
    private readonly InMemoryNotificationRepository _repository;
    private readonly NotificationWriter _writer;
    private readonly ObjectId _adminUserId;

    public SloBreachNotificationServiceTests()
    {
        _repository = new InMemoryNotificationRepository();
        _writer = new NotificationWriter(_repository);
        _adminUserId = ObjectId.GenerateNewId();
    }

    private SloBreachNotificationService CreateService(int cooldownMinutes = 60)
    {
        var options = Options.Create(new SloBreachMonitorOptions
        {
            BreachNotificationCooldownMinutes = cooldownMinutes
        });
        return new SloBreachNotificationService(
            _writer,
            options,
            NullLogger<SloBreachNotificationService>.Instance);
    }

    // ── First breach fires notification ───────────────────────────────────

    [Fact]
    public async Task First_breach_fires_notification()
    {
        var service = CreateService();

        var result = await service.ReportBreachAsync(
            "TestSLO",
            observedP95: 250.0,
            target: 200.0,
            _adminUserId,
            detectionTime: DateTime.UtcNow);

        Assert.Equal(SloBreachResult.Notified, result);

        var all = _repository.GetAll();
        var note = Assert.Single(all);
        Assert.Equal(NotificationType.AdminSloBreach, note.NotificationType);
        Assert.True(note.IsAdminNotification);
        Assert.Equal(_adminUserId, note.UserId);
        Assert.Contains("TestSLO", note.Content);
        Assert.Contains("250.0", note.Content);
        Assert.Contains("200", note.Content);
    }

    // ── Second breach within cooldown is suppressed ───────────────────────

    [Fact]
    public async Task Second_breach_within_cooldown_is_suppressed()
    {
        var service = CreateService(cooldownMinutes: 60);

        // First breach — should fire.
        var first = await service.ReportBreachAsync(
            "LatencySLO",
            observedP95: 350.0,
            target: 200.0,
            _adminUserId,
            detectionTime: DateTime.UtcNow);

        Assert.Equal(SloBreachResult.Notified, first);
        Assert.Single(_repository.GetAll());

        // Second breach of the same SLO immediately — should be suppressed.
        var second = await service.ReportBreachAsync(
            "LatencySLO",
            observedP95: 400.0,
            target: 200.0,
            _adminUserId,
            detectionTime: DateTime.UtcNow);

        Assert.Equal(SloBreachResult.Suppressed, second);

        // No additional notification written.
        Assert.Single(_repository.GetAll());
    }

    // ── Different SLOs have independent cooldowns ─────────────────────────

    [Fact]
    public async Task Different_slos_have_independent_cooldowns()
    {
        var service = CreateService(cooldownMinutes: 60);

        var first = await service.ReportBreachAsync(
            "SLO-A", observedP95: 150.0, target: 100.0, _adminUserId, DateTime.UtcNow);
        Assert.Equal(SloBreachResult.Notified, first);

        var second = await service.ReportBreachAsync(
            "SLO-B", observedP95: 150.0, target: 100.0, _adminUserId, DateTime.UtcNow);
        Assert.Equal(SloBreachResult.Notified, second);

        // Both should have fired (different SLO, independent cooldowns).
        Assert.Equal(2, _repository.GetAll().Count);
    }

    // ── Breach after cooldown expires fires again ─────────────────────────

    [Fact]
    public async Task Breach_after_cooldown_expires_fires_again()
    {
        // Use a very short cooldown (1 minute) so we can simulate expiry.
        var service = CreateService(cooldownMinutes: 1);

        var detectionTime = DateTime.UtcNow;

        // First breach — fires.
        var first = await service.ReportBreachAsync(
            "CooldownSLO", observedP95: 300.0, target: 200.0, _adminUserId, detectionTime);
        Assert.Equal(SloBreachResult.Notified, first);
        Assert.Single(_repository.GetAll());

        // Manipulate the cooldown state to simulate elapsed time.
        // Set last fire to 65 minutes ago so cooldown has expired.
        service.LastFiredPerSlo["CooldownSLO"] = DateTime.UtcNow.AddMinutes(-65);

        // Second breach — should fire again (cooldown expired).
        var second = await service.ReportBreachAsync(
            "CooldownSLO", observedP95: 310.0, target: 200.0, _adminUserId, detectionTime);
        Assert.Equal(SloBreachResult.Notified, second);

        // Second notification written.
        Assert.Equal(2, _repository.GetAll().Count);
    }

    // ── New calendar day resets cooldown ──────────────────────────────────

    [Fact]
    public async Task New_calendar_day_resets_cooldown()
    {
        var service = CreateService(cooldownMinutes: 60);

        var detectionTime = DateTime.UtcNow;

        // First breach — fires.
        var first = await service.ReportBreachAsync(
            "DailySLO", observedP95: 250.0, target: 200.0, _adminUserId, detectionTime);
        Assert.Equal(SloBreachResult.Notified, first);
        Assert.Single(_repository.GetAll());

        // Simulate: last fire was yesterday (previous calendar day).
        service.LastFiredPerSlo["DailySLO"] = DateTime.UtcNow.Date.AddDays(-1).AddHours(10);

        // Breach on new day — should fire (previous day's cooldown is irrelevant).
        var second = await service.ReportBreachAsync(
            "DailySLO", observedP95: 260.0, target: 200.0, _adminUserId, detectionTime);
        Assert.Equal(SloBreachResult.Notified, second);

        // Second notification written.
        Assert.Equal(2, _repository.GetAll().Count);
    }

    // ── SLO breach content includes all required fields ───────────────────

    [Fact]
    public async Task Breach_content_includes_all_required_fields()
    {
        var service = CreateService();

        var detectionTime = DateTime.UtcNow;

        var result = await service.ReportBreachAsync(
            "DataSync Latency",
            observedP95: 1845.3,
            target: 1000.0,
            _adminUserId,
            detectionTime);

        Assert.Equal(SloBreachResult.Notified, result);

        var note = Assert.Single(_repository.GetAll());
        Assert.Contains("DataSync Latency", note.Content);
        Assert.Contains("1845.3", note.Content);
        Assert.Contains("1000", note.Content);
        Assert.Contains("Detected at:", note.Content);
        Assert.Contains("This is not investment advice.", note.Content);
    }

    // ── Rejects null SLO name ─────────────────────────────────────────────

    [Fact]
    public async Task Throws_on_null_slo_name()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.ReportBreachAsync(
                null!, observedP95: 100, target: 200, _adminUserId, DateTime.UtcNow));
    }
}
