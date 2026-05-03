using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the snapshot-consistent equity base reader (REQ-RME-006a/b/c/d)
/// and its integration into the RME advisory service.
///
/// Uses Moq to mock <see cref="IMongoDatabase"/> / <see cref="IMongoCollection{TDocument}"/>
/// via <see cref="IMongoCollection{TDocument}.FindAsync"/> (the <c>Find</c> method was
/// moved to an extension method in MongoDB 3.0).
/// </summary>
public sealed class EquityBaseReaderTests : IDisposable
{
    private readonly Meter _meter = new($"SignalStack.Worker.Tests.{Guid.NewGuid()}");

    void IDisposable.Dispose() => _meter.Dispose();

    // ─── REQ-RME-006a: Auto-derived equity ─────────────────────────────────

    [Theory]
    [InlineData(100000.00, 100000.00, EquityBaseSource.AutoDerived)]
    [InlineData(250000.00, 250000.00, EquityBaseSource.AutoDerived)]
    public async Task GetEquityBaseAsync_uses_equity_curve_when_available(
        decimal equityValue, decimal expectedBase, EquityBaseSource expectedSource)
    {
        // Arrange
        var userId = "test_user_1";
        var db = new Mock<IMongoDatabase>();

        // Seed equity_curve with a record
        var equityDoc = new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = "2026-05-02",
            ["equity_value"] = (BsonDecimal128)equityValue,
        };

        // Seed user with ledger_snapshot_version = 1
        var userDoc = new BsonDocument
        {
            ["user_id"] = userId,
            ["ledger_snapshot_version"] = 1L,
            ["status"] = "approved",
        };

        SetupCollection(db, "equity_curve", equityDoc);
        SetupCollection(db, "users", userDoc);
        SetupCollection(db, "portfolio_snapshots", null);
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object);

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert
        Assert.Equal(expectedBase, result.EquityBase);
        Assert.Equal(expectedSource, result.Source);
        Assert.True(result.IsSnapshotConsistent);
        Assert.Equal(0, result.SnapshotRetryAttempts);
        Assert.Equal(equityValue, result.FyersTotalAccountValue);
        Assert.Equal(equityValue, result.PlatformVisibleEquity);
        Assert.Equal(0, result.ExternalHoldingsExcluded);
        Assert.False(result.IsOverrideActive);
        Assert.False(result.IsOverrideStale);
    }

    [Fact]
    public async Task GetEquityBaseAsync_falls_back_to_portfolio_snapshots_when_no_equity_curve()
    {
        // Arrange
        var userId = "test_user_2";
        var db = new Mock<IMongoDatabase>();

        // equity_curve returns null -> forces fallback
        SetupCollection(db, "equity_curve", null);

        // Seed portfolio_snapshots with market value and cash
        var snapshotDoc = new BsonDocument
        {
            ["user_id"] = userId,
            ["current_market_value"] = (BsonDecimal128)80000m,
            ["cash_reserve"] = (BsonDecimal128)20000m,
        };

        // Seed user
        var userDoc = new BsonDocument
        {
            ["user_id"] = userId,
            ["ledger_snapshot_version"] = 1L,
            ["status"] = "approved",
        };

        SetupCollection(db, "portfolio_snapshots", snapshotDoc);
        SetupCollection(db, "users", userDoc);
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object);

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert
        Assert.Equal(100000m, result.EquityBase); // 80000 + 20000
        Assert.Equal(EquityBaseSource.AutoDerived, result.Source);
        Assert.True(result.IsSnapshotConsistent);
        Assert.Equal(100000m, result.FyersTotalAccountValue);
        Assert.Equal(100000m, result.PlatformVisibleEquity);
    }

    [Fact]
    public async Task GetEquityBaseAsync_returns_zero_when_no_data_available()
    {
        // Arrange
        var userId = "new_user_no_data";
        var db = new Mock<IMongoDatabase>();

        // No equity_curve, no portfolio_snapshots
        SetupCollection(db, "equity_curve", null);
        SetupCollection(db, "portfolio_snapshots", null);

        // Seed user only
        var userDoc = new BsonDocument
        {
            ["user_id"] = userId,
            ["ledger_snapshot_version"] = 1L,
            ["status"] = "approved",
        };
        SetupCollection(db, "users", userDoc);
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object);

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert
        Assert.Equal(0, result.EquityBase);
        Assert.Equal(EquityBaseSource.Zero, result.Source);
        Assert.True(result.IsSnapshotConsistent);
        Assert.Null(result.FyersTotalAccountValue);
        Assert.Null(result.PlatformVisibleEquity);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("could not be determined", result.AdvisoryMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ─── REQ-RME-006c: Manual override ─────────────────────────────────────

    [Fact]
    public async Task GetEquityBaseAsync_uses_override_when_active()
    {
        // Arrange
        var userId = "override_user";
        var db = new Mock<IMongoDatabase>();

        SetupCollection(db, "equity_curve", new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = "2026-05-02",
            ["equity_value"] = (BsonDecimal128)100000m,
        });

        SetupCollection(db, "users", new BsonDocument
        {
            ["user_id"] = userId,
            ["ledger_snapshot_version"] = 1L,
            ["status"] = "approved",
            ["equity_base_override"] = (BsonDecimal128)200000m,
            ["equity_base_override_updated_at"] = DateTime.UtcNow,
        });

        SetupCollection(db, "portfolio_snapshots", null);
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object);

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert
        Assert.Equal(200000m, result.EquityBase); // Override value used
        Assert.Equal(EquityBaseSource.ManualOverride, result.Source);
        Assert.True(result.IsOverrideActive);
        Assert.False(result.IsOverrideStale);
        // Auto-derived equity still visible
        Assert.Equal(100000m, result.PlatformVisibleEquity);
    }

    [Fact]
    public async Task GetEquityBaseAsync_marks_stale_override()
    {
        // Arrange
        var userId = "stale_override_user";
        var db = new Mock<IMongoDatabase>();

        SetupCollection(db, "equity_curve", new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = "2026-05-01",
            ["equity_value"] = (BsonDecimal128)100000m,
        });

        // Override set 31 days ago (past the 30-day staleness threshold)
        var staleDate = DateTime.UtcNow.AddDays(-31);
        SetupCollection(db, "users", new BsonDocument
        {
            ["user_id"] = userId,
            ["ledger_snapshot_version"] = 1L,
            ["status"] = "approved",
            ["equity_base_override"] = (BsonDecimal128)200000m,
            ["equity_base_override_updated_at"] = staleDate,
        });

        SetupCollection(db, "portfolio_snapshots", null);

        // Set stale days to 30 via sys_config
        SetupSysConfig(db, "30");

        var reader = CreateReader(db.Object);

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert
        Assert.Equal(200000m, result.EquityBase);
        Assert.Equal(EquityBaseSource.ManualOverride, result.Source);
        Assert.True(result.IsOverrideActive);
        Assert.True(result.IsOverrideStale);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("not been reviewed", result.AdvisoryMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ─── REQ-RME-006d: Snapshot-consistent read protocol ───────────────────

    [Fact]
    public async Task GetEquityBaseAsync_consistent_on_first_read_when_V1_equals_V2()
    {
        // Arrange
        var userId = "consistent_user";
        var db = new Mock<IMongoDatabase>();

        SetupCollection(db, "equity_curve", new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = "2026-05-01",
            ["equity_value"] = (BsonDecimal128)50000m,
        });

        // Version stays the same across all reads
        var userDoc = new BsonDocument
        {
            ["user_id"] = userId,
            ["ledger_snapshot_version"] = 5L,
            ["status"] = "approved",
        };
        SetupCollection(db, "users", userDoc);
        SetupCollection(db, "portfolio_snapshots", null);
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object);

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert
        Assert.True(result.IsSnapshotConsistent);
        Assert.Equal(0, result.SnapshotRetryAttempts);
        Assert.Equal(50000m, result.EquityBase);
    }

    [Fact]
    public async Task GetEquityBaseAsync_retries_when_V1_differs_from_V2()
    {
        // Arrange
        var userId = "changing_user";
        var db = new Mock<IMongoDatabase>();

        SetupCollection(db, "equity_curve", new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = "2026-05-01",
            ["equity_value"] = (BsonDecimal128)75000m,
        });

        SetupCollection(db, "portfolio_snapshots", null);
        SetupSysConfig(db, null);

        // Users collection returns different versions on subsequent FindAsync calls
        var callCount = 0;
        var usersCollection = new Mock<IMongoCollection<BsonDocument>>();
        SetupFindAsync(usersCollection, () =>
        {
            callCount++;
            return callCount switch
            {
                1 => new BsonDocument { ["ledger_snapshot_version"] = 1L, ["status"] = "approved" },
                _ => new BsonDocument { ["ledger_snapshot_version"] = 2L, ["status"] = "approved" },
            };
        });

        db.Setup(d => d.GetCollection<BsonDocument>("users", It.IsAny<MongoCollectionSettings?>()))
            .Returns(usersCollection.Object);

        var reader = CreateReader(db.Object);

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert — should succeed after retry (V1 and V2 converge)
        Assert.True(result.IsSnapshotConsistent);
        Assert.True(result.SnapshotRetryAttempts >= 1);
        Assert.Equal(75000m, result.EquityBase);
    }

    // ─── REQ-RME-006d: Override-active retry exhaustion message (RME-R1) ───

    [Fact]
    public async Task GetEquityBaseAsync_override_active_retry_exhaustion_shows_differentiated_message()
    {
        // Arrange
        var userId = "override_exhausted";
        var db = new Mock<IMongoDatabase>();

        SetupCollection(db, "equity_curve", new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = "2026-05-01",
            ["equity_value"] = (BsonDecimal128)100000m,
        });

        // Each FindAsync call on users increments version so retries are always exhausted
        var callCount = 0;
        var usersCollection = new Mock<IMongoCollection<BsonDocument>>();
        SetupFindAsync(usersCollection, () =>
        {
            callCount++;
            return new BsonDocument
            {
                ["ledger_snapshot_version"] = (long)callCount,
                ["status"] = "approved",
                ["equity_base_override"] = (BsonDecimal128)200000m,
                ["equity_base_override_updated_at"] = DateTime.UtcNow,
            };
        });

        db.Setup(d => d.GetCollection<BsonDocument>("users", It.IsAny<MongoCollectionSettings?>()))
            .Returns(usersCollection.Object);

        SetupCollection(db, "portfolio_snapshots", null);
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object, maxRetries: 0); // 0 retries = immediate exhaustion

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert
        Assert.Equal(EquityBaseSource.SnapshotContentionDeferred, result.Source);
        Assert.False(result.IsSnapshotConsistent);
        // RME-R1: override-active differentiated message
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("Your equity override is in effect", result.AdvisoryMessage);
        Assert.Contains("retrying", result.AdvisoryMessage);
    }

    [Fact]
    public async Task GetEquityBaseAsync_retry_exhaustion_shows_generic_message_when_no_override()
    {
        // Arrange
        var userId = "no_override_exhausted";
        var db = new Mock<IMongoDatabase>();

        SetupCollection(db, "equity_curve", new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = "2026-05-01",
            ["equity_value"] = (BsonDecimal128)100000m,
        });

        // Each FindAsync call on users increments version
        var callCount = 0;
        var usersCollection = new Mock<IMongoCollection<BsonDocument>>();
        SetupFindAsync(usersCollection, () =>
        {
            callCount++;
            return new BsonDocument
            {
                ["ledger_snapshot_version"] = (long)callCount,
                ["status"] = "approved",
            };
        });

        db.Setup(d => d.GetCollection<BsonDocument>("users", It.IsAny<MongoCollectionSettings?>()))
            .Returns(usersCollection.Object);

        SetupCollection(db, "portfolio_snapshots", null);
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object, maxRetries: 0);

        // Act
        var result = await reader.GetEquityBaseAsync(userId);

        // Assert
        Assert.Equal(EquityBaseSource.SnapshotContentionDeferred, result.Source);
        Assert.False(result.IsSnapshotConsistent);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("Portfolio state is updating", result.AdvisoryMessage);
    }

    // ─── Advisory service integration ──────────────────────────────────────

    [Fact]
    public async Task RmeAdvisoryService_returns_sizing_based_on_equity_base()
    {
        // Arrange
        var userId = "sizing_user";
        var db = new Mock<IMongoDatabase>();

        SetupCollection(db, "equity_curve", new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = "2026-05-02",
            ["equity_value"] = (BsonDecimal128)200000m,
        });

        SetupCollection(db, "users", new BsonDocument
        {
            ["user_id"] = userId,
            ["ledger_snapshot_version"] = 1L,
            ["status"] = "approved",
        });

        SetupCollection(db, "portfolio_snapshots", null);
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object);
        var options = Options.Create(new RmeModuleOptions
        {
            DefaultRiskPerTradePct = 1.0, // 1% of equity
            MaxRiskPerTradePct = 1.0,
            MinRiskPerTradePct = 0.5,
        });

        var service = new RmeAdvisoryService(
            reader,
            options,
            NullLogger<RmeAdvisoryService>.Instance);

        // Act
        var advisory = await service.GetSizingRecommendationAsync(
            userId, "RELIANCE", 2500m,
            "fixed_percentage", "fixed_stop",
            default);

        // Assert — equity base = 200000, risk = 1% → risk amount = 2000
        Assert.Equal(200000m * 0.01m, advisory.RiskAmount);
        Assert.Equal("fixed_percentage", advisory.SizingModelUsed);
        Assert.Equal("fixed_stop", advisory.StopTypeUsed);
    }

    [Fact]
    public async Task RmeAdvisoryService_blocks_sizing_when_equity_is_zero()
    {
        // Arrange
        var userId = "zero_equity_user";
        var db = new Mock<IMongoDatabase>();

        SetupCollection(db, "equity_curve", null);
        SetupCollection(db, "portfolio_snapshots", null);
        SetupCollection(db, "users", new BsonDocument
        {
            ["user_id"] = userId,
            ["ledger_snapshot_version"] = 1L,
            ["status"] = "approved",
        });
        SetupSysConfig(db, null);

        var reader = CreateReader(db.Object);
        var options = Options.Create(new RmeModuleOptions { DefaultRiskPerTradePct = 1.0 });

        var service = new RmeAdvisoryService(
            reader,
            options,
            NullLogger<RmeAdvisoryService>.Instance);

        // Act
        var advisory = await service.GetSizingRecommendationAsync(
            userId, "RELIANCE", 2500m,
            "fixed_percentage", "fixed_stop",
            default);

        // Assert — sizing blocked
        Assert.Equal(0m, advisory.RecommendedQuantity);
        Assert.Equal(0m, advisory.RiskAmount);
        Assert.NotNull(advisory.AdvisoryMessage);
        Assert.Contains("could not be determined", advisory.AdvisoryMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private EquityBaseReader CreateReader(
        IMongoDatabase database,
        int maxRetries = 3,
        int backoffMs = 10)
    {
        var options = Options.Create(new EquityReadOptions
        {
            SnapshotRetryMaxAttempts = maxRetries,
            SnapshotRetryBackoffMs = backoffMs,
        });

        return new EquityBaseReader(
            database,
            options,
            NullLogger<EquityBaseReader>.Instance);
    }

    /// <summary>
    /// Sets up <paramref name="db"/> so that
    /// <c>GetCollection&lt;BsonDocument&gt;(collectionName, ...)</c> returns
    /// a mock collection whose <c>FindAsync().MoveNextAsync()/Current</c>
    /// returns <paramref name="returnDoc"/>.
    /// </summary>
    private static void SetupCollection(
        Mock<IMongoDatabase> db,
        string collectionName,
        BsonDocument? returnDoc)
    {
        var collection = new Mock<IMongoCollection<BsonDocument>>();
        SetupFindAsync(collection, () => returnDoc);

        db.Setup(d => d.GetCollection<BsonDocument>(
                collectionName,
                It.IsAny<MongoCollectionSettings?>()))
            .Returns(collection.Object);
    }

    /// <summary>
    /// Sets up sys_config so <c>FindAsync().Current</c> returns
    /// a doc with the given stale-days value (or null if <paramref name="staleDaysValue"/> is null).
    /// </summary>
    private static void SetupSysConfig(Mock<IMongoDatabase> db, string? staleDaysValue)
    {
        BsonDocument? doc = staleDaysValue is not null
            ? new BsonDocument
            {
                ["key"] = "risk.equity_override_stale_days",
                ["value"] = staleDaysValue,
            }
            : null;

        SetupCollection(db, "sys_config", doc);
    }

    /// <summary>
    /// Configures <paramref name="collection"/> so that <c>FindAsync</c> returns
    /// an async cursor whose <c>Current</c> is provided by <paramref name="resultFactory"/>
    /// (called each time the cursor is advanced).
    /// </summary>
    private static void SetupFindAsync(
        Mock<IMongoCollection<BsonDocument>> collection,
        Func<BsonDocument?> resultFactory)
    {
        // Create an IAsyncCursor mock that yields one document per factory call.
        var cursor = new Mock<IAsyncCursor<BsonDocument>>();
        cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        cursor.Setup(c => c.Current)
            .Returns(() =>
            {
                var doc = resultFactory();
                return doc is not null ? new[] { doc } : Array.Empty<BsonDocument>();
            });

        // The MongoDB 3.0 Find extension method calls FindAsync on the collection.
        collection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);
    }
}
