using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Storage.LedgerWriters;

/// <summary>
/// MongoDB implementation of <see cref="ITradeLedgerRepository"/>.
///
/// All trades carry the <c>ledger_fence_token</c> for stale-write audit
/// (REQ-PORT-031b(b)). Dedup uses the compound unique index on
/// <c>(user_id, trade_id)</c> — a <c>MongoWriteException</c> with duplicate
/// key error is caught and logged at debug level, making ingestion idempotent
/// (REQ-PORT-023).
///
/// The fencing-token guard write that precedes data insertion is the caller's
/// responsibility (performed via <c>fyers_account_sync</c> in each sync service).
/// </summary>
public sealed class MongoTradeLedgerRepository : ITradeLedgerRepository
{
    private readonly IMongoCollection<TradeLedgerDocument> _collection;
    private readonly ILogger<MongoTradeLedgerRepository> _logger;

    public MongoTradeLedgerRepository(IMongoDatabase database, ILogger<MongoTradeLedgerRepository> logger)
    {
        _collection = database.GetCollection<TradeLedgerDocument>("trade_ledger");
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> InsertTradeAsync(TradeLedgerDocument trade, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trade);

        try
        {
            await _collection.InsertOneAsync(trade, cancellationToken: ct);
            return true;
        }
        catch (MongoWriteException ex) when (IsDuplicateKeyError(ex))
        {
            _logger.LogDebug(
                "Duplicate trade skipped: user={UserId} trade_id={TradeId}.",
                trade.UserId, trade.TradeId);
            return false;
        }
    }

    public async Task<BulkInsertResult> InsertTradesAsync(
        IReadOnlyList<TradeLedgerDocument> trades, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trades);

        if (trades.Count == 0)
            return new BulkInsertResult(0, 0);

        var inserted = 0;
        var skipped = 0;

        foreach (var trade in trades)
        {
            try
            {
                await _collection.InsertOneAsync(trade, cancellationToken: ct);
                inserted++;
            }
            catch (MongoWriteException ex) when (IsDuplicateKeyError(ex))
            {
                _logger.LogDebug(
                    "Duplicate trade skipped during bulk insert: user={UserId} trade_id={TradeId}.",
                    trade.UserId, trade.TradeId);
                skipped++;
            }
        }

        _logger.LogInformation(
            "Bulk trade insert complete: {Inserted} inserted, {Skipped} duplicates skipped.",
            inserted, skipped);

        return new BulkInsertResult(inserted, skipped);
    }

    public async Task<bool> TradeExistsAsync(string userId, string tradeId, CancellationToken ct = default)
    {
        var filter = Builders<TradeLedgerDocument>.Filter.Eq(t => t.UserId, userId)
                   & Builders<TradeLedgerDocument>.Filter.Eq(t => t.TradeId, tradeId);

        return await _collection.Find(filter).AnyAsync(ct);
    }

    public async Task<List<TradeLedgerDocument>> GetTradesByUserAsync(
        string userId, CancellationToken ct = default)
    {
        var filter = Builders<TradeLedgerDocument>.Filter.Eq(t => t.UserId, userId);
        var sort = Builders<TradeLedgerDocument>.Sort.Ascending(t => t.TradeAt);

        return await _collection.Find(filter).Sort(sort).ToListAsync(ct);
    }

    public async Task<List<TradeLedgerDocument>> GetTradesByUserAndSymbolAsync(
        string userId, string symbol, CancellationToken ct = default)
    {
        var filter = Builders<TradeLedgerDocument>.Filter.Eq(t => t.UserId, userId)
                   & Builders<TradeLedgerDocument>.Filter.Eq(t => t.Symbol, symbol);

        var sort = Builders<TradeLedgerDocument>.Sort.Ascending(t => t.TradeAt);

        return await _collection.Find(filter).Sort(sort).ToListAsync(ct);
    }

    /// <summary>
    /// Determines whether a <see cref="MongoWriteException"/> was caused by a
    /// duplicate key error (error code 11000).
    /// </summary>
    private static bool IsDuplicateKeyError(MongoWriteException ex)
        => ex.WriteError?.Category == ServerErrorCategory.DuplicateKey
           || ex.WriteError?.Code == 11000;
}
