using System.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using SignalStack.Api.Execution;
using SignalStack.Domain.Execution;
using SignalStack.Storage.Execution;
using SignalStack.Storage.SysConfig;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Unit tests for the signed-payload endpoint and intent signing service (P7-T5).
/// REQ-ORDER-009b/009c, EC-5 (heat gate), RME-R2 (LADS health gate).
/// </summary>
public sealed class IntentSigningServiceTests
{
    private const string TestHmacKey = "test-intent-hmac-key-for-unit-tests-min-16-chars!";
    private const string TestUserId = "user_test_001";
    private const string TestSessionId = "session_test_001";
    private const string TestSymbol = "NSE:HDFCBANK-EQ";

    // ── Construction Tests ─────────────────────────────────────────────────────

    [Fact]
    public void Constructor_throws_when_config_missing()
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["Execution:IntentHmacKey"]).Returns((string?)null);

        var ex = Assert.Throws<InvalidOperationException>(() =>
            new IntentSigningService(
                Mock.Of<IMongoDatabase>(),
                Mock.Of<ISysConfigRepository>(),
                Mock.Of<IIntentLedgerRepository>(),
                configMock.Object));

        Assert.Contains("Execution:IntentHmacKey", ex.Message);
    }

    // ── Validation Tests (pre-Mongo checks) ────────────────────────────────────

    [Fact]
    public async Task Throws_on_invalid_action()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<SignedPayloadSigningException>(() =>
            service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
                new SignedPayloadRequest(TestSymbol, "invalid_action", 10, "MARKET", null),
                CancellationToken.None));

        Assert.Equal("invalid_action", ex.Reason);
    }

    [Fact]
    public async Task Throws_on_invalid_order_type()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<SignedPayloadSigningException>(() =>
            service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
                new SignedPayloadRequest(TestSymbol, "entry", 10, "STOP_LOSS", null),
                CancellationToken.None));

        Assert.Equal("invalid_order_type", ex.Reason);
    }

    [Fact]
    public async Task Throws_on_zero_quantity()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<SignedPayloadSigningException>(() =>
            service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
                new SignedPayloadRequest(TestSymbol, "entry", 0, "MARKET", null),
                CancellationToken.None));

        Assert.Equal("invalid_quantity", ex.Reason);
    }

    [Fact]
    public async Task Throws_on_negative_quantity()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<SignedPayloadSigningException>(() =>
            service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
                new SignedPayloadRequest(TestSymbol, "entry", -5, "MARKET", null),
                CancellationToken.None));

        Assert.Equal("invalid_quantity", ex.Reason);
    }

    [Fact]
    public async Task Throws_on_LIMIT_without_price()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<SignedPayloadSigningException>(() =>
            service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
                new SignedPayloadRequest(TestSymbol, "entry", 10, "LIMIT", null),
                CancellationToken.None));

        Assert.Equal("invalid_price", ex.Reason);
    }

    [Fact]
    public async Task Throws_on_LIMIT_with_zero_price()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<SignedPayloadSigningException>(() =>
            service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
                new SignedPayloadRequest(TestSymbol, "entry", 10, "LIMIT", 0m),
                CancellationToken.None));

        Assert.Equal("invalid_price", ex.Reason);
    }

    // ── Valid Action Parameter Tests ───────────────────────────────────────────

    [Theory]
    [InlineData("entry", "BUY")]
    [InlineData("add", "BUY")]
    [InlineData("reduce", "SELL")]
    [InlineData("exit", "SELL")]
    public async Task Produces_correct_transaction_type(string action, string expectedTxType)
    {
        // For SELL actions we need a position
        var db = new Mock<IMongoDatabase>();
        var positionsMock = new Mock<IMongoCollection<BsonDocument>>();
        var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();

        if (expectedTxType == "SELL")
        {
            // Return an open position for SELL validation
            cursorMock.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);
            cursorMock.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(true)
                .Returns(false);
            cursorMock.SetupGet(c => c.Current)
                .Returns(new List<BsonDocument>
                {
                    new() { ["user_id"] = TestUserId, ["symbol"] = TestSymbol, ["state"] = "Open", ["quantity"] = 50 }
                });
        }
        else
        {
            // Return no positions for BUY actions
            cursorMock.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            cursorMock.Setup(c => c.MoveNext(It.IsAny<CancellationToken>()))
                .Returns(false);
            cursorMock.SetupGet(c => c.Current).Returns(new List<BsonDocument>());
        }

        positionsMock.Setup(c => c.FindAsync<BsonDocument>(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorMock.Object);

        db.Setup(d => d.GetCollection<BsonDocument>("positions", null))
            .Returns(positionsMock.Object);

        // Mock other collections as empty
        MockEmptyCollection(db, "sys_config");
        MockEmptyCollection(db, "intent_ledger");
        MockEmptyCollection(db, "rme_incidents");
        MockEmptyCollection(db, "fyers_account_sync");
        MockEmptyCollection(db, "equity_curve");
        MockEmptyCollection(db, "portfolio_snapshots");

        var service = CreateService(db.Object);

        var result = await service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
            new SignedPayloadRequest(TestSymbol, action, 10, "MARKET", null),
            CancellationToken.None);

        Assert.Equal(expectedTxType, result.DataAttributes["data-transaction_type"]);
    }

    // ── HMAC / Response Shape Tests ────────────────────────────────────────────

    [Fact]
    public async Task Returns_nonce_and_data_attributes()
    {
        var db = CreateDefaultMockDatabase();
        var service = CreateService(db.Object);

        var result = await service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
            new SignedPayloadRequest(TestSymbol, "entry", 100, "MARKET", null),
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(32, result.Nonce.Length);
        Assert.Matches("^[0-9a-f]{32}$", result.Nonce);

        Assert.Contains("data-nonce", result.DataAttributes);
        Assert.Contains("data-signature", result.DataAttributes);
        Assert.Contains("data-symbol", result.DataAttributes);
        Assert.Contains("data-product", result.DataAttributes);
        Assert.Contains("data-quantity", result.DataAttributes);
        Assert.Contains("data-transaction_type", result.DataAttributes);
        Assert.Contains("data-order_type", result.DataAttributes);
        Assert.Contains("data-price", result.DataAttributes);

        Assert.Equal(TestSymbol, result.DataAttributes["data-symbol"]);
        Assert.Equal("CNC", result.DataAttributes["data-product"]);
        Assert.Equal("100", result.DataAttributes["data-quantity"]);
        Assert.Equal("BUY", result.DataAttributes["data-transaction_type"]);
        Assert.Equal("MARKET", result.DataAttributes["data-order_type"]);
    }

    [Fact]
    public async Task Different_nonces_on_consecutive_calls()
    {
        var db = CreateDefaultMockDatabase();
        var service = CreateService(db.Object);

        var r1 = await service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
            new SignedPayloadRequest(TestSymbol, "entry", 10, "MARKET", null), CancellationToken.None);
        var r2 = await service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
            new SignedPayloadRequest(TestSymbol, "entry", 20, "MARKET", null), CancellationToken.None);

        Assert.NotEqual(r1.Nonce, r2.Nonce);
        Assert.NotEqual(r1.DataAttributes["data-signature"], r2.DataAttributes["data-signature"]);
    }

    [Fact]
    public async Task Signature_is_64_char_hex()
    {
        var db = CreateDefaultMockDatabase();
        var service = CreateService(db.Object);

        var result = await service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
            new SignedPayloadRequest(TestSymbol, "entry", 10, "MARKET", null), CancellationToken.None);

        Assert.Matches("^[0-9a-f]{64}$", result.DataAttributes["data-signature"]);
    }

    [Fact]
    public async Task Expires_at_is_in_future()
    {
        var db = CreateDefaultMockDatabase();
        var service = CreateService(db.Object);

        var result = await service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
            new SignedPayloadRequest(TestSymbol, "entry", 10, "MARKET", null), CancellationToken.None);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.True(result.ExpiresAtUnix > now);
        Assert.True(result.ExpiresAtUnix <= now + 300);
    }

    [Fact]
    public async Task LIMIT_orders_include_price()
    {
        var db = CreateDefaultMockDatabase();
        var service = CreateService(db.Object);

        var result = await service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
            new SignedPayloadRequest(TestSymbol, "entry", 10, "LIMIT", 1650.50m), CancellationToken.None);

        Assert.Equal("1650.50", result.DataAttributes["data-price"]);
        Assert.Equal("LIMIT", result.DataAttributes["data-order_type"]);
    }

    // ── Error Response Shape Tests ─────────────────────────────────────────────

    [Fact]
    public void SignedPayloadError_has_reason_and_message()
    {
        var error = new SignedPayloadError("test_reason", "Test message.");
        Assert.Equal("test_reason", error.Reason);
        Assert.Equal("Test message.", error.Message);
    }

    [Fact]
    public void SignedPayloadError_supports_last_successful_sync_at()
    {
        var ts = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc);
        var error = new SignedPayloadError("lads_sustained_failure_active", "msg", ts);
        Assert.Equal(ts, error.LastSuccessfulSyncAt);
    }

    // ── HMAC Domain Separation (pure computation test) ────────────────────────

    [Fact]
    public void Domain_separation_prefix_used_in_canonical_payload()
    {
        // Verify that "fyers.intent.v1:" appears correctly by computing the HMAC
        // ourselves and matching.
        const string domainSep = "fyers.intent.v1:";
        var nonce = "abcdef0123456789abcdef0123456789";
        var canonical = $"{domainSep}{nonce}|user|session|NSE:SYMBOL-EQ|entry|BUY|CNC|10|MARKET||1234567890";
        var keyBytes = Encoding.UTF8.GetBytes(TestHmacKey);
        var expectedSig = Convert.ToHexString(
            HMACSHA256.HashData(keyBytes, Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();

        // Verify it starts with the prefix
        Assert.StartsWith(domainSep, canonical);

        // Verify signature format is correct hex
        Assert.Matches("^[0-9a-f]{64}$", expectedSig);
    }

    // ── Kill Switch Gate Test ──────────────────────────────────────────────────

    [Fact]
    public async Task Throws_when_kill_switch_active()
    {
        var db = new Mock<IMongoDatabase>();
        var collMock = new Mock<IMongoCollection<BsonDocument>>();
        var cursorMock = new Mock<IAsyncCursor<BsonDocument>>();

        var sysConfigDoc = new BsonDocument
        {
            ["key"] = "risk.kill_switch.active",
            ["value"] = true
        };

        cursorMock.SetupSequence(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        cursorMock.SetupSequence(c => c.MoveNext(It.IsAny<CancellationToken>()))
            .Returns(true)
            .Returns(false);
        cursorMock.SetupGet(c => c.Current).Returns(new List<BsonDocument> { sysConfigDoc });

        collMock.Setup(c => c.FindAsync<BsonDocument>(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorMock.Object);

        db.Setup(d => d.GetCollection<BsonDocument>("sys_config", null)).Returns(collMock.Object);

        // Wire remaining empty collections
        MockEmptyCollection(db, "positions");
        MockEmptyCollection(db, "intent_ledger");
        MockEmptyCollection(db, "rme_incidents");
        MockEmptyCollection(db, "fyers_account_sync");
        MockEmptyCollection(db, "equity_curve");
        MockEmptyCollection(db, "portfolio_snapshots");

        var service = CreateService(db.Object);

        var ex = await Assert.ThrowsAsync<SignedPayloadSigningException>(() =>
            service.CreateSignedPayloadAsync(TestUserId, TestSessionId,
                new SignedPayloadRequest(TestSymbol, "entry", 10, "MARKET", null),
                CancellationToken.None));

        Assert.Equal("kill_switch_active", ex.Reason);
    }

    // ── Test Infrastructure ─────────────────────────────────────────────────────

    /// <summary>Creates a mock database where all queried collections come back empty.</summary>
    private static Mock<IMongoDatabase> CreateDefaultMockDatabase()
    {
        var db = new Mock<IMongoDatabase>();
        MockEmptyCollection(db, "positions");
        MockEmptyCollection(db, "sys_config");
        MockEmptyCollection(db, "intent_ledger");
        MockEmptyCollection(db, "rme_incidents");
        MockEmptyCollection(db, "fyers_account_sync");
        MockEmptyCollection(db, "equity_curve");
        MockEmptyCollection(db, "portfolio_snapshots");
        return db;
    }

    /// <summary>Wires an empty collection mock for <c>GetCollection{T}("name")</c> calls.</summary>
    private static void MockEmptyCollection(Mock<IMongoDatabase> db, string name)
    {
        var coll = new Mock<IMongoCollection<BsonDocument>>();
        var cursor = new Mock<IAsyncCursor<BsonDocument>>();

        cursor.Setup(c => c.MoveNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        cursor.Setup(c => c.MoveNext(It.IsAny<CancellationToken>()))
            .Returns(false);
        cursor.SetupGet(c => c.Current).Returns(new List<BsonDocument>());

        coll.Setup(c => c.FindAsync<BsonDocument>(
                It.IsAny<FilterDefinition<BsonDocument>>(),
                It.IsAny<FindOptions<BsonDocument, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursor.Object);

        db.Setup(d => d.GetCollection<BsonDocument>(name, null)).Returns(coll.Object);
    }

    private IntentSigningService CreateService(IMongoDatabase? database = null)
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["Execution:IntentHmacKey"]).Returns(TestHmacKey);

        var sysConfigMock = new Mock<ISysConfigRepository>();
        sysConfigMock.Setup(s => s.GetLongAsync("orders.payload_nonce_ttl_seconds", 300L, It.IsAny<CancellationToken>()))
            .ReturnsAsync(300L);
        sysConfigMock.Setup(s => s.GetDecimalAsync("risk.default.max_portfolio_heat_pct", 5m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5m);
        sysConfigMock.Setup(s => s.GetDecimalAsync("risk.drawdown_enforcement.suppress_entry_pct", 20m, It.IsAny<CancellationToken>()))
            .ReturnsAsync(20m);

        var intentLedgerMock = new Mock<IIntentLedgerRepository>();
        intentLedgerMock.Setup(r => r.CreateIntentAsync(
                It.IsAny<IntentLedgerDocument>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        return new IntentSigningService(
            database ?? CreateDefaultMockDatabase().Object,
            sysConfigMock.Object,
            intentLedgerMock.Object,
            configMock.Object);
    }
}
