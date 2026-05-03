using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the equity-base divergence detection and advisory notification
/// (REQ-RME-006e).
/// </summary>
public sealed class EquityBaseDivergenceServiceTests
{
    // ─── No divergence when values are equal ─────────────────────────────────

    [Fact]
    public async Task AssessDivergenceAsync_returns_no_divergence_when_values_equal()
    {
        // Arrange
        var userId = "507f1f77bcf86cd799439011";
        var result = new EquityBaseResult
        {
            EquityBase = 100000m,
            Source = EquityBaseSource.AutoDerived,
            IsSnapshotConsistent = true,
            SnapshotRetryAttempts = 0,
            FyersTotalAccountValue = 100000m,
            PlatformVisibleEquity = 100000m,
            ExternalHoldingsExcluded = 0m,
            IsOverrideActive = false,
            IsOverrideStale = false,
        };

        var db = new Mock<IMongoDatabase>();
        SetupSysConfig(db, null); // No sys_config row → uses default 15%

        var service = CreateService(db.Object);

        // Act
        var assessment = await service.AssessDivergenceAsync(userId, result);

        // Assert
        Assert.False(assessment.HasDivergence);
        Assert.Equal(0m, assessment.GapPercent);
        Assert.Equal(DivergenceAction.None, assessment.Action);
    }

    // ─── Divergence detected when gap exceeds threshold ──────────────────────

    [Fact]
    public async Task AssessDivergenceAsync_detects_divergence_when_gap_exceeds_threshold()
    {
        // Arrange
        var userId = "507f1f77bcf86cd799439011";
        var result = new EquityBaseResult
        {
            EquityBase = 100000m,
            Source = EquityBaseSource.AutoDerived,
            IsSnapshotConsistent = true,
            SnapshotRetryAttempts = 0,
            FyersTotalAccountValue = 150000m,  // 50% higher than platform
            PlatformVisibleEquity = 100000m,
            ExternalHoldingsExcluded = 50000m,
            IsOverrideActive = false,
            IsOverrideStale = false,
        };

        var db = new Mock<IMongoDatabase>();

        SetupSysConfig(db, null); // Default 15% threshold — gap is 50% → exceeds

        // Set up notifications collection AFTER SetupSysConfig so it wins.
        var notificationsCollection = new Mock<IMongoCollection<BsonDocument>>();
        BsonDocument? writtenNotification = null;
        notificationsCollection
            .Setup(c => c.InsertOneAsync(
                It.IsAny<BsonDocument>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<BsonDocument, InsertOneOptions, CancellationToken>(
                (doc, _, _) => writtenNotification = doc)
            .Returns(Task.CompletedTask);

        db.Setup(d => d.GetCollection<BsonDocument>("notifications", It.IsAny<MongoCollectionSettings?>()))
            .Returns(notificationsCollection.Object);

        var service = CreateService(db.Object);

        // Act
        var assessment = await service.AssessDivergenceAsync(userId, result);

        // Assert
        Assert.True(assessment.HasDivergence);
        Assert.Equal(150000m, assessment.FyersTotal);
        Assert.Equal(100000m, assessment.PlatformVisible);
        Assert.Equal(50m, assessment.GapPercent);  // (50000 / 100000) * 100 = 50%
        Assert.Equal(15m, assessment.WarnThresholdPct);
        Assert.Equal(DivergenceAction.AdvisoryWritten, assessment.Action);

        // Verify notification was written
        Assert.NotNull(writtenNotification);
        Assert.Equal("equity_base_divergence_advisory",
            writtenNotification["notification_type"].AsString);
        Assert.False(writtenNotification["is_admin_notification"].AsBoolean);
        var content = writtenNotification["content"].AsString;
        Assert.Contains("₹", content);
        Assert.Contains("50", content);       // 50% gap (the exact formatting is locale-dependent)
        Assert.Equal("/profile/settings", writtenNotification["deep_link"].AsString);
        Assert.Equal("/profile/settings", writtenNotification["deep_link"].AsString);
    }

    // ─── No divergence when values are null ──────────────────────────────────

    [Fact]
    public async Task AssessDivergenceAsync_returns_no_divergence_when_values_unavailable()
    {
        // Arrange
        var userId = "507f1f77bcf86cd799439011";
        var result = new EquityBaseResult
        {
            EquityBase = 0m,
            Source = EquityBaseSource.Zero,
            IsSnapshotConsistent = true,
            SnapshotRetryAttempts = 0,
            FyersTotalAccountValue = null,
            PlatformVisibleEquity = null,
            ExternalHoldingsExcluded = null,
            IsOverrideActive = false,
            IsOverrideStale = false,
        };

        var db = new Mock<IMongoDatabase>();
        SetupSysConfig(db, null);

        var service = CreateService(db.Object);

        // Act
        var assessment = await service.AssessDivergenceAsync(userId, result);

        // Assert
        Assert.False(assessment.HasDivergence);
        Assert.Null(assessment.GapPercent);
        Assert.Equal(DivergenceAction.None, assessment.Action);
    }

    // ─── No notification when gap is below threshold ─────────────────────────

    [Fact]
    public async Task AssessDivergenceAsync_does_not_write_notification_when_gap_below_threshold()
    {
        // Arrange
        var userId = "507f1f77bcf86cd799439011";
        var result = new EquityBaseResult
        {
            EquityBase = 200000m,
            Source = EquityBaseSource.AutoDerived,
            IsSnapshotConsistent = true,
            SnapshotRetryAttempts = 0,
            FyersTotalAccountValue = 220000m,  // 10% gap — below 15% threshold
            PlatformVisibleEquity = 200000m,
            ExternalHoldingsExcluded = 20000m,
            IsOverrideActive = false,
            IsOverrideStale = false,
        };

        var db = new Mock<IMongoDatabase>();
        SetupSysConfig(db, null);

        var notificationsCollection = new Mock<IMongoCollection<BsonDocument>>();
        BsonDocument? writtenNotification = null;
        notificationsCollection
            .Setup(c => c.InsertOneAsync(
                It.IsAny<BsonDocument>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<BsonDocument, InsertOneOptions, CancellationToken>(
                (doc, _, _) => writtenNotification = doc)
            .Returns(Task.CompletedTask);

        db.Setup(d => d.GetCollection<BsonDocument>("notifications", It.IsAny<MongoCollectionSettings?>()))
            .Returns(notificationsCollection.Object);

        var service = CreateService(db.Object);

        // Act
        var assessment = await service.AssessDivergenceAsync(userId, result);

        // Assert
        Assert.False(assessment.HasDivergence);
        Assert.Equal(10m, assessment.GapPercent);
        Assert.Equal(DivergenceAction.None, assessment.Action);
        Assert.Null(writtenNotification);  // No notification written
    }

    // ─── Uses custom threshold from sys_config ───────────────────────────────

    [Fact]
    public async Task AssessDivergenceAsync_uses_custom_threshold_from_sys_config()
    {
        // Arrange
        var userId = "507f1f77bcf86cd799439011";
        var result = new EquityBaseResult
        {
            EquityBase = 100000m,
            Source = EquityBaseSource.AutoDerived,
            IsSnapshotConsistent = true,
            SnapshotRetryAttempts = 0,
            FyersTotalAccountValue = 110000m,  // 10% gap
            PlatformVisibleEquity = 100000m,
            ExternalHoldingsExcluded = 10000m,
            IsOverrideActive = false,
            IsOverrideStale = false,
        };

        var db = new Mock<IMongoDatabase>();

        // Set custom threshold of 5% — 10% gap exceeds this
        SetupSysConfig(db, "5");

        var notificationsCollection = new Mock<IMongoCollection<BsonDocument>>();
        notificationsCollection
            .Setup(c => c.InsertOneAsync(
                It.IsAny<BsonDocument>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        db.Setup(d => d.GetCollection<BsonDocument>("notifications", It.IsAny<MongoCollectionSettings?>()))
            .Returns(notificationsCollection.Object);

        var service = CreateService(db.Object);

        // Act
        var assessment = await service.AssessDivergenceAsync(userId, result);

        // Assert
        Assert.True(assessment.HasDivergence);
        Assert.Equal(10m, assessment.GapPercent);
        Assert.Equal(5m, assessment.WarnThresholdPct);
        Assert.Equal(DivergenceAction.AdvisoryWritten, assessment.Action);
    }

    // ─── No divergence when platform_visible is zero ─────────────────────────

    [Fact]
    public async Task AssessDivergenceAsync_returns_no_divergence_when_platform_visible_is_zero()
    {
        // Arrange
        var userId = "507f1f77bcf86cd799439011";
        var result = new EquityBaseResult
        {
            EquityBase = 0m,
            Source = EquityBaseSource.AutoDerived,
            IsSnapshotConsistent = true,
            SnapshotRetryAttempts = 0,
            FyersTotalAccountValue = 100000m,
            PlatformVisibleEquity = 0m,
            ExternalHoldingsExcluded = 100000m,
            IsOverrideActive = false,
            IsOverrideStale = false,
        };

        var db = new Mock<IMongoDatabase>();
        SetupSysConfig(db, null);

        var service = CreateService(db.Object);

        // Act
        var assessment = await service.AssessDivergenceAsync(userId, result);

        // Assert
        Assert.False(assessment.HasDivergence);
        Assert.Null(assessment.GapPercent);  // Divisible by zero — no assessment
        Assert.Equal(DivergenceAction.None, assessment.Action);
    }

    // ─── Absolute divergence: handles negative gap (platform > FYERS) ────────

    [Fact]
    public async Task AssessDivergenceAsync_uses_absolute_value_for_gap()
    {
        // Arrange
        var userId = "507f1f77bcf86cd799439011";
        var result = new EquityBaseResult
        {
            EquityBase = 200000m,
            Source = EquityBaseSource.AutoDerived,
            IsSnapshotConsistent = true,
            SnapshotRetryAttempts = 0,
            FyersTotalAccountValue = 100000m,  // FYERS < platform (unusual but possible)
            PlatformVisibleEquity = 200000m,
            ExternalHoldingsExcluded = -100000m,
            IsOverrideActive = false,
            IsOverrideStale = false,
        };

        var db = new Mock<IMongoDatabase>();
        SetupSysConfig(db, null);

        var service = CreateService(db.Object);

        // Act
        var assessment = await service.AssessDivergenceAsync(userId, result);

        // Assert
        Assert.True(assessment.HasDivergence);
        Assert.Equal(50m, assessment.GapPercent);  // |100000 - 200000| / 200000 * 100 = 50%
        Assert.Equal(DivergenceAction.AdvisoryWritten, assessment.Action);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static EquityBaseDivergenceService CreateService(IMongoDatabase database)
    {
        return new EquityBaseDivergenceService(
            database,
            NullLogger<EquityBaseDivergenceService>.Instance);
    }

    /// <summary>
    /// Sets up sys_config so <c>FindAsync</c> returns a doc with the given
    /// divergence threshold value (or null if <paramref name="thresholdPct"/> is null).
    /// </summary>
    private static void SetupSysConfig(Mock<IMongoDatabase> db, string? thresholdPct)
    {
        BsonDocument? doc = thresholdPct is not null
            ? new BsonDocument
            {
                ["key"] = "risk.equity_base.external_divergence_warn_pct",
                ["value"] = thresholdPct,
            }
            : null;

        var collection = new Mock<IMongoCollection<BsonDocument>>();
        SetupFindAsync(collection, () => doc);

        db.Setup(d => d.GetCollection<BsonDocument>(
                "sys_config",
                It.IsAny<MongoCollectionSettings?>()))
            .Returns(collection.Object);

        // Set up all other collections as empty to avoid null refs
        var emptyCollection = new Mock<IMongoCollection<BsonDocument>>();
        SetupFindAsync(emptyCollection, () => null);

        // Only stub "notifications" when we haven't already set it up
        db.Setup(d => d.GetCollection<BsonDocument>(
                It.Is<string>(s => s != "sys_config"),
                It.IsAny<MongoCollectionSettings?>()))
            .Returns(emptyCollection.Object);
    }

    private static void SetupFindAsync(
        Mock<IMongoCollection<BsonDocument>> collection,
        Func<BsonDocument?> resultFactory)
    {
        var cursor = new Mock<IAsyncCursor<BsonDocument>>();
        cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        cursor.Setup(c => c.Current)
            .Returns(() =>
            {
                var doc = resultFactory();
                return doc is not null ? new[] { doc } : Array.Empty<BsonDocument>();
            });

        collection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);
    }
}
