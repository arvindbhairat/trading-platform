using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the DrawdownTracker (P6-T20) against the six graduated drawdown
/// thresholds (REQ-DRDN-003), loss-limit advisories (REQ-DRDN-002), and
/// sys_config key naming convention (REQ-DRDN-004).
///
/// Uses Moq to mock <see cref="IMongoDatabase"/> / <see cref="IMongoCollection{TDocument}"/>.
/// </summary>
public sealed class DrawdownTrackerTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a DrawdownTracker with the given mocked MongoDB.
    /// </summary>
    private static DrawdownTracker CreateTracker(Mock<IMongoDatabase> db)
    {
        return new DrawdownTracker(
            db.Object,
            NullLogger<DrawdownTracker>.Instance);
    }

    /// <summary>
    /// Sets up equity_curve collection to return a list of documents
    /// through the FindAsync + ToListAsync path.
    /// Uses a cursor that returns all docs on first MoveNextAsync then completes.
    /// </summary>
    private static void SetupEquityCurve(
        Mock<IMongoDatabase> db,
        string userId,
        List<(string Date, decimal Equity)> records)
    {
        var docs = records.Select(r => new BsonDocument
        {
            ["user_id"] = userId,
            ["session_date"] = r.Date,
            ["equity_value"] = (BsonDecimal128)r.Equity,
        }).ToList();

        var collection = new Mock<IMongoCollection<BsonDocument>>();
        var cursor = SetupCursor(docs);

        collection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);

        db.Setup(d => d.GetCollection<BsonDocument>("equity_curve", It.IsAny<MongoCollectionSettings?>()))
            .Returns(collection.Object);
    }

    /// <summary>
    /// Sets up sys_config collection to return a single document.
    /// </summary>
    private static void SetupSysConfig(Mock<IMongoDatabase> db, string key, string value)
    {
        var doc = new BsonDocument
        {
            ["key"] = key,
            ["value"] = value,
        };

        var collection = new Mock<IMongoCollection<BsonDocument>>();
        var cursor = SetupCursor(new[] { doc });

        collection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);

        db.Setup(d => d.GetCollection<BsonDocument>("sys_config", It.IsAny<MongoCollectionSettings?>()))
            .Returns(collection.Object);
    }

    /// <summary>
    /// Creates a cursor that returns the given documents once, then completes.
    /// </summary>
    private static Mock<IAsyncCursor<T>> SetupCursor<T>(IReadOnlyList<T> items)
    {
        var cursor = new Mock<IAsyncCursor<T>>();
        var moveNextCallCount = 0;

        cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                moveNextCallCount++;
                return moveNextCallCount == 1; // true once, then false
            });

        cursor.Setup(c => c.Current)
            .Returns(() => moveNextCallCount == 1 ? items : Enumerable.Empty<T>());

        cursor.Setup(c => c.Dispose())
            .Callback(() => { /* no-op */ });

        return cursor;
    }

    /// <summary>
    /// Sets up sys_config so FindAsync returns no matching documents.
    /// Used to test default fallback behaviour.
    /// </summary>
    private static void SetupSysConfigEmpty(Mock<IMongoDatabase> db)
    {
        var collection = new Mock<IMongoCollection<BsonDocument>>();
        var cursor = SetupCursor(Array.Empty<BsonDocument>());

        collection.Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);

        db.Setup(d => d.GetCollection<BsonDocument>("sys_config", It.IsAny<MongoCollectionSettings?>()))
            .Returns(collection.Object);
    }

    /// <summary>
    /// Generates a list of equity curve records simulating a peak-then-decline
    /// pattern for testing graduated thresholds. Records are returned in
    /// DESCENDING date order (newest first) to match the MongoDB sort
    /// that the tracker's ReadEquityHistoryAsync applies.
    /// </summary>
    /// <param name="highWaterMark">The peak equity value.</param>
    /// <param name="currentEquity">The latest equity value.</param>
    /// <param name="numSessions">Total number of trading sessions to simulate.</param>
    private static List<(string Date, decimal Equity)> GenerateEquityHistory(
        decimal highWaterMark,
        decimal currentEquity,
        int numSessions = 30)
    {
        // Build in chronological order first: climb to HWM, then decline.
        var chronological = new List<decimal>(numSessions);

        // Phase 1: climb to HWM (first ~40% of sessions)
        var climbSessions = (int)(numSessions * 0.4);
        for (int i = 0; i < climbSessions; i++)
        {
            var equity = highWaterMark * (0.7m + 0.3m * i / Math.Max(1, climbSessions - 1));
            chronological.Add(Math.Round(equity, 2));
        }

        // Phase 2: decline from HWM to currentEquity (remaining ~60%)
        var declineSessions = numSessions - climbSessions;
        for (int i = 0; i < declineSessions; i++)
        {
            var equity = highWaterMark - (highWaterMark - currentEquity) * (i + 1) / declineSessions;
            chronological.Add(Math.Round(equity, 2));
        }

        // Reverse to descending order (newest first) — matches MongoDB sort.
        chronological.Reverse();

        return chronological.Select((eq, i) =>
        {
            var day = 30 - i;
            return ($"2026-01-{day:D2}", eq);
        }).ToList();
    }

    // ─── No data scenarios ──────────────────────────────────────────────────

    [Fact]
    public async Task AssessAsync_returns_empty_when_no_equity_data()
    {
        // Arrange
        var db = new Mock<IMongoDatabase>();
        var userId = "no_data_user";

        // equity_curve returns empty
        SetupEquityCurve(db, userId, new List<(string, decimal)>());

        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.Equal(0m, result.CurrentDrawdownPct);
        Assert.Equal(0m, result.CurrentEquity);
        Assert.False(result.SizeReductionActive);
        Assert.False(result.EntrySuppressed);
        Assert.False(result.StopAllTradingAdvisory);
        Assert.Equal(0, result.HighestThresholdLevel);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("No equity curve data", result.AdvisoryMessage);
    }

    // ─── REQ-DRDN-001: HWM and drawdown computation ─────────────────────────

    [Fact]
    public async Task AssessAsync_computes_drawdown_from_high_water_mark()
    {
        // Arrange: HWM = 100000, current equity = 85000 → 15% drawdown
        var userId = "drawdown_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 85000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.Equal(100000m, result.HighWaterMark);
        Assert.Equal(85000m, result.CurrentEquity);
        Assert.InRange(result.CurrentDrawdownPct, 14.9m, 15.1m);
    }

    [Fact]
    public async Task AssessAsync_returns_zero_drawdown_at_high_water_mark()
    {
        // Arrange: equity is flat at 100000 (no decline)
        var userId = "flat_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 100000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.Equal(0m, result.CurrentDrawdownPct);
        Assert.Equal(0, result.HighestThresholdLevel);
        Assert.True(result.IsAtHighWaterMark);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── REQ-DRDN-003: Six graduated thresholds ─────────────────────────────

    [Fact]
    public async Task AssessAsync_no_thresholds_below_5_pct()
    {
        // Arrange: 3% drawdown — below all thresholds
        var userId = "low_dd_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 97000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.InRange(result.CurrentDrawdownPct, 2.9m, 3.1m);
        Assert.False(result.SizeReductionActive);
        Assert.False(result.AddonSuppressed);
        Assert.False(result.ReducePositionsAdvisory);
        Assert.False(result.EntrySuppressed);
        Assert.False(result.CloseWeakestAdvisory);
        Assert.False(result.StopAllTradingAdvisory);
        Assert.Equal(0, result.HighestThresholdLevel);
    }

    [Fact]
    public async Task AssessAsync_size_reduction_active_at_5_pct()
    {
        // Arrange: 5% drawdown — level 1 threshold
        var userId = "level1_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 95000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.InRange(result.CurrentDrawdownPct, 4.9m, 5.1m);
        Assert.True(result.SizeReductionActive);     // Category (a)
        Assert.False(result.AddonSuppressed);
        Assert.False(result.ReducePositionsAdvisory);
        Assert.False(result.EntrySuppressed);
        Assert.False(result.CloseWeakestAdvisory);
        Assert.False(result.StopAllTradingAdvisory);
        Assert.Equal(1, result.HighestThresholdLevel);
    }

    [Fact]
    public async Task AssessAsync_addon_suppressed_at_10_pct()
    {
        // Arrange: 10% drawdown — level 2 threshold
        var userId = "level2_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 90000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.InRange(result.CurrentDrawdownPct, 9.9m, 10.1m);
        Assert.True(result.SizeReductionActive);     // Category (a)
        Assert.True(result.AddonSuppressed);          // Category (a)
        Assert.False(result.ReducePositionsAdvisory);
        Assert.False(result.EntrySuppressed);
        Assert.False(result.CloseWeakestAdvisory);
        Assert.False(result.StopAllTradingAdvisory);
        Assert.Equal(2, result.HighestThresholdLevel);
    }

    [Fact]
    public async Task AssessAsync_reduce_positions_advisory_at_15_pct()
    {
        // Arrange: 15% drawdown — level 3 threshold
        var userId = "level3_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 85000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.InRange(result.CurrentDrawdownPct, 14.9m, 15.1m);
        Assert.True(result.SizeReductionActive);         // Category (a)
        Assert.True(result.AddonSuppressed);              // Category (a)
        Assert.True(result.ReducePositionsAdvisory);      // Category (b)
        Assert.False(result.EntrySuppressed);
        Assert.False(result.CloseWeakestAdvisory);
        Assert.False(result.StopAllTradingAdvisory);
        Assert.Equal(3, result.HighestThresholdLevel);
    }

    [Fact]
    public async Task AssessAsync_entry_suppressed_at_20_pct()
    {
        // Arrange: 20% drawdown — level 4 threshold
        var userId = "level4_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 80000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.InRange(result.CurrentDrawdownPct, 19.9m, 20.1m);
        Assert.True(result.SizeReductionActive);         // Category (a)
        Assert.True(result.AddonSuppressed);              // Category (a)
        Assert.True(result.ReducePositionsAdvisory);      // Category (b)
        Assert.True(result.EntrySuppressed);              // Category (a)
        Assert.False(result.CloseWeakestAdvisory);
        Assert.False(result.StopAllTradingAdvisory);
        Assert.Equal(4, result.HighestThresholdLevel);
    }

    [Fact]
    public async Task AssessAsync_close_weakest_advisory_at_25_pct()
    {
        // Arrange: 25% drawdown — level 5 threshold
        var userId = "level5_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 75000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.InRange(result.CurrentDrawdownPct, 24.9m, 25.1m);
        Assert.True(result.SizeReductionActive);         // Category (a)
        Assert.True(result.AddonSuppressed);              // Category (a)
        Assert.True(result.ReducePositionsAdvisory);      // Category (b)
        Assert.True(result.EntrySuppressed);              // Category (a)
        Assert.True(result.CloseWeakestAdvisory);         // Category (b)
        Assert.False(result.StopAllTradingAdvisory);
        Assert.Equal(5, result.HighestThresholdLevel);
    }

    [Fact]
    public async Task AssessAsync_stop_all_trading_advisory_at_30_pct()
    {
        // Arrange: 30% drawdown — level 6 threshold
        var userId = "level6_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 70000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.InRange(result.CurrentDrawdownPct, 29.9m, 30.1m);
        Assert.True(result.SizeReductionActive);         // Category (a)
        Assert.True(result.AddonSuppressed);              // Category (a)
        Assert.True(result.ReducePositionsAdvisory);      // Category (b)
        Assert.True(result.EntrySuppressed);              // Category (a)
        Assert.True(result.CloseWeakestAdvisory);         // Category (b)
        Assert.True(result.StopAllTradingAdvisory);       // Category (b)
        Assert.Equal(6, result.HighestThresholdLevel);
    }

    // ─── REQ-DRDN-002: Loss-limit advisories ────────────────────────────────

    [Fact]
    public async Task AssessAsync_reports_daily_loss_limit_breach()
    {
        // Arrange: Build a curve where the last session drops sharply.
        // Build descending (MongoDB sort order) — date desc, so newest first.
        var userId = "daily_loss_user";
        var db = new Mock<IMongoDatabase>();

        // Chronological equity: 80000, 85000, 90000, 95000, 110000, 105000
        // Last session drops from 110000 to 105000 = ~4.55% daily loss > 2% limit.
        // Passed in DESCENDING date order (simulates MongoDB sort).
        var records = new List<(string Date, decimal Equity)>
        {
            ("2026-01-30", 105000m),
            ("2026-01-29", 110000m),
            ("2026-01-28",  95000m),
            ("2026-01-27",  90000m),
            ("2026-01-24",  85000m),
            ("2026-01-23",  80000m),
        };

        SetupEquityCurve(db, userId, records);
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert: daily loss = (110000 - 105000) / 110000 ≈ 4.55%
        Assert.NotNull(result.DailyLossLimit);
        Assert.InRange(result.DailyLossLimit.CurrentLossPct, 4.0m, 5.0m);
        Assert.True(result.DailyLossLimit.IsBreached,
            $"Expected daily loss limit breach. Limit: {result.DailyLossLimit.LimitPct}%, " +
            $"Current: {result.DailyLossLimit.CurrentLossPct}%");
        Assert.Equal("daily", result.DailyLossLimit.Period);
    }

    [Fact]
    public async Task AssessAsync_daily_loss_limit_not_breached_when_within_limit()
    {
        // Arrange: small daily decline — within 2% limit
        var userId = "daily_safe_user";
        var db = new Mock<IMongoDatabase>();

        // Very shallow decline: last session drops by <1%
        var records = GenerateEquityHistory(100000m, 99500m, 10);
        SetupEquityCurve(db, userId, records);
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.NotNull(result.DailyLossLimit);
        Assert.False(result.DailyLossLimit.IsBreached);
    }

    // ─── REQ-DRDN-004: Custom threshold values from sys_config ─────────────

    [Fact]
    public async Task AssessAsync_uses_custom_thresholds_from_sys_config()
    {
        // Arrange: sys_config has custom size_reduction threshold at 8%.
        // At ~22% drawdown, the 8% custom threshold is breached.
        var userId = "custom_threshold_user";
        var db = new Mock<IMongoDatabase>();

        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 78000m));
        SetupSysConfigEmpty(db);
        // Override size_reduction threshold
        SetupSysConfig(db, "risk.drawdown_enforcement.size_reduction_pct", "8");

        var tracker = CreateTracker(db);
        var result = await tracker.AssessAsync(userId);

        // At 22% drawdown, 8% reduction threshold is breached
        Assert.True(result.SizeReductionActive);
        Assert.InRange(result.CurrentDrawdownPct, 21.9m, 22.1m);
    }

    // ─── Per-user isolation ─────────────────────────────────────────────────

    [Fact]
    public async Task AssessAsync_isolates_per_user()
    {
        // Arrange: verify each user gets their own drawdown assessment
        // by creating independent mock setups per user.
        var trackerA = CreateTracker(SetupDrawdownDb("user_a", 100000m, 95000m));
        var trackerB = CreateTracker(SetupDrawdownDb("user_b", 100000m, 80000m));

        // Act
        var resultA = await trackerA.AssessAsync("user_a");
        var resultB = await trackerB.AssessAsync("user_b");

        // Assert
        Assert.InRange(resultA.CurrentDrawdownPct, 4.9m, 5.1m);
        Assert.Equal(1, resultA.HighestThresholdLevel);

        Assert.InRange(resultB.CurrentDrawdownPct, 19.9m, 20.1m);
        Assert.Equal(4, resultB.HighestThresholdLevel);
    }

    /// <summary>
    /// Creates a mocked MongoDB with equity curve data for the given user
    /// at the specified drawdown level.
    /// </summary>
    private static Mock<IMongoDatabase> SetupDrawdownDb(
        string userId, decimal hwm, decimal currentEquity)
    {
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(hwm, currentEquity));
        SetupSysConfigEmpty(db);
        return db;
    }

    // ─── Advisory message generation ────────────────────────────────────────

    [Fact]
    public async Task AssessAsync_generates_advisory_message_at_deep_drawdown()
    {
        // Arrange: 25% drawdown — multiple levels hit
        var userId = "advisory_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 75000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("Category (a)", result.AdvisoryMessage);
        Assert.Contains("Category (b)", result.AdvisoryMessage);
        Assert.Contains("position", result.AdvisoryMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AssessAsync_no_advisory_at_high_water_mark()
    {
        // Arrange: no drawdown
        var userId = "no_dd_user";
        var db = new Mock<IMongoDatabase>();
        SetupEquityCurve(db, userId, GenerateEquityHistory(100000m, 100000m));
        SetupSysConfigEmpty(db);

        var tracker = CreateTracker(db);

        // Act
        var result = await tracker.AssessAsync(userId);

        // Assert
        Assert.Null(result.AdvisoryMessage);
    }
}
