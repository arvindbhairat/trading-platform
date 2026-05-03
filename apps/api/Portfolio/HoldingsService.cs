using Microsoft.Extensions.Logging;
using SignalStack.Api.Historical;
using SignalStack.Api.LedgerWriters;
using SignalStack.Configuration.Ledger;

namespace SignalStack.Api.Portfolio;

/// <summary>
/// Computes current holdings from the immutable trade ledger using FIFO matching
/// and current market prices from SQL Server.
///
/// REQ-DASH-002/010: portfolio summary and per-position data from trade ledger.
/// REQ-DASH-013: data freshness is reflected via the price source timestamp.
/// </summary>
public sealed class HoldingsService
{
    private readonly ITradeLedgerRepository _ledger;
    private readonly IOhlcvRepository _ohlcv;
    private readonly ILogger<HoldingsService> _logger;

    public HoldingsService(
        ITradeLedgerRepository ledger,
        IOhlcvRepository ohlcv,
        ILogger<HoldingsService> logger)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _ohlcv = ohlcv ?? throw new ArgumentNullException(nameof(ohlcv));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Computes all open holdings for a user by applying FIFO matching
    /// against the trade ledger, then resolving current prices.
    /// </summary>
    public async Task<List<Holding>> ComputeHoldingsAsync(
        string userId, CancellationToken ct = default)
    {
        var trades = await _ledger.GetTradesByUserAsync(userId, ct);
        var fifoLots = ApplyFifo(trades);
        return await ResolvePricesAsync(fifoLots, ct);
    }

    /// <summary>
    /// Computes the holding for a specific symbol using FIFO matching.
    /// </summary>
    public async Task<Holding?> ComputeHoldingAsync(
        string userId, string symbol, CancellationToken ct = default)
    {
        var trades = await _ledger.GetTradesByUserAndSymbolAsync(userId, symbol, ct);
        var fifoLots = ApplyFifo(trades);
        var holdings = await ResolvePricesAsync(fifoLots, ct);
        return holdings.FirstOrDefault();
    }

    /// <summary>
    /// Computes portfolio-level summary from all open holdings.
    /// </summary>
    public async Task<PortfolioSummary> ComputeSummaryAsync(
        string userId, CancellationToken ct = default)
    {
        var holdings = await ComputeHoldingsAsync(userId, ct);

        var totalInvested = holdings.Sum(h => h.TotalInvested);
        var totalMarketValue = holdings.All(h => h.CurrentMarketValue.HasValue)
            ? holdings.Sum(h => h.CurrentMarketValue!.Value)
            : (decimal?)null;
        var totalPnl = totalMarketValue.HasValue
            ? totalMarketValue.Value - totalInvested
            : (decimal?)null;
        var totalPnlPct = totalInvested > 0 && totalPnl.HasValue
            ? Math.Round((totalPnl.Value / totalInvested) * 100, 2)
            : (decimal?)null;

        return new PortfolioSummary
        {
            TotalInvested = totalInvested,
            TotalMarketValue = totalMarketValue,
            TotalUnrealizedPnl = totalPnl,
            TotalUnrealizedPnlPercent = totalPnlPct,
            PositionCount = holdings.Count,
        };
    }

    /// <summary>
    /// Applies FIFO matching to a chronological list of trades.
    /// Returns open buy lots per symbol.
    /// </summary>
    private static Dictionary<string, List<OpenLotState>> ApplyFifo(
        List<TradeLedgerDocument> trades)
    {
        var result = new Dictionary<string, List<OpenLotState>>();

        foreach (var trade in trades.OrderBy(t => t.TradeAt))
        {
            if (!result.ContainsKey(trade.Symbol))
                result[trade.Symbol] = new List<OpenLotState>();

            var lots = result[trade.Symbol];

            if (string.Equals(trade.Side, "buy", StringComparison.OrdinalIgnoreCase))
            {
                lots.Add(new OpenLotState
                {
                    RemainingQuantity = trade.Quantity,
                    Price = trade.Price,
                    TradeId = trade.TradeId,
                    BuyDate = trade.TradeAt,
                });
            }
            else if (string.Equals(trade.Side, "sell", StringComparison.OrdinalIgnoreCase))
            {
                var remainingSell = trade.Quantity;
                var survivingLots = new List<OpenLotState>();

                foreach (var lot in lots)
                {
                    if (remainingSell <= 0)
                    {
                        survivingLots.Add(lot);
                        continue;
                    }

                    var matchedQty = Math.Min(lot.RemainingQuantity, remainingSell);
                    remainingSell -= matchedQty;
                    var newRemQty = lot.RemainingQuantity - matchedQty;

                    if (newRemQty > 0)
                    {
                        survivingLots.Add(lot with { RemainingQuantity = newRemQty });
                    }
                }

                result[trade.Symbol] = survivingLots;
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves current prices for each symbol with open lots and builds Holding records.
    /// </summary>
    private async Task<List<Holding>> ResolvePricesAsync(
        Dictionary<string, List<OpenLotState>> fifoLots, CancellationToken ct)
    {
        var holdings = new List<Holding>();

        foreach (var (symbol, lots) in fifoLots)
        {
            if (lots.Count == 0) continue;

            var totalQty = lots.Sum(l => l.RemainingQuantity);
            var avgPrice = lots.Sum(l => l.RemainingQuantity * l.Price) / totalQty;
            var firstBuyDate = lots.Min(l => l.BuyDate);
            var holdingDays = (int)(DateTime.UtcNow - firstBuyDate).TotalDays;

            // Fetch current price from SQL Server closing prices.
            decimal? currentPrice = null;
            try
            {
                var lastCandleDate = await _ohlcv.GetLastCandleDateAsync(symbol, ct);
                if (lastCandleDate.HasValue)
                {
                    var candles = await _ohlcv.GetDailyAsync(
                        symbol, lastCandleDate.Value, lastCandleDate.Value, ct);
                    if (candles.Count > 0)
                    {
                        currentPrice = Convert.ToDecimal(candles[^1].Close);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to fetch current price for {Symbol}: {Message}",
                    symbol, ex.Message);
            }

            holdings.Add(new Holding
            {
                Symbol = symbol,
                Quantity = totalQty,
                AverageBuyPrice = Math.Round(avgPrice, 2),
                CurrentPrice = currentPrice.HasValue
                    ? Math.Round(currentPrice.Value, 2)
                    : null,
                HoldingPeriodDays = holdingDays,
                Lots = lots.Select(l => new OpenLot
                {
                    TradeId = l.TradeId,
                    RemainingQuantity = l.RemainingQuantity,
                    BuyPrice = Math.Round(l.Price, 2),
                    BuyDate = l.BuyDate,
                }).ToList(),
            });
        }

        return holdings;
    }

    /// <summary>Internal mutable state for FIFO computation.</summary>
    private sealed record OpenLotState
    {
        public int RemainingQuantity { get; set; }
        public decimal Price { get; init; }
        public string TradeId { get; init; } = "";
        public DateTime BuyDate { get; init; }
    }
}
