using SignalStack.Api.Notifications;
using SignalStack.Domain.Notifications;
using SignalStack.Notifications;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Tests for the Telegram notification template builder.
/// REQ-NOTIFY-024: entry-filled template with all 8 required fields + partial-fill variant.
/// REQ-NOTIFY-025: exit-advisory template with all 7 required fields.
/// Both REQs state the field set is locked to prevent Phase 4 / Phase 6 format divergence.
/// </summary>
public sealed class NotificationTemplateBuilderTests
{
    // ── Entry-fill template (REQ-NOTIFY-024) ──────────────────────────────

    [Fact]
    public void BuildEntryFillContent_includes_all_required_fields()
    {
        var data = new EntryFillData(
            Symbol: "RELIANCE",
            ExchangeCode: "NSE",
            Side: "BUY",
            FilledQuantity: 100,
            RequestedQuantity: null,
            AverageFillPrice: 2450.00m,
            StopLevel: 2325.00m,
            RiskPercent: 1.5m,
            RiskAmount: 3675.00m,
            IntentSource: "Signal: MA Crossover (Daily)",
            DeepLink: "/chart?symbol=RELIANCE"
        );

        var content = NotificationTemplateBuilder.BuildEntryFillContent(data);

        // REQ-NOTIFY-024(a): symbol and exchange code
        Assert.Contains("RELIANCE", content);
        Assert.Contains("NSE", content);

        // REQ-NOTIFY-024(b): transaction side
        Assert.Contains("BUY", content);

        // REQ-NOTIFY-024(c): filled quantity
        Assert.Contains("100", content);

        // REQ-NOTIFY-024(d): weighted average fill price in INR, 2 decimal places
        Assert.Contains("₹2450.00", content);

        // REQ-NOTIFY-024(e): initial RME stop level in INR, 2 decimal places
        Assert.Contains("₹2325.00", content);

        // REQ-NOTIFY-024(f): position risk as % of equity (1 decimal) and INR amount
        Assert.Contains("1.5%", content);
        Assert.Contains("₹3675.00", content);

        // REQ-NOTIFY-024(g): intent source
        Assert.Contains("Signal: MA Crossover (Daily)", content);

        // REQ-NOTIFY-024(h): portal deep link
        Assert.Contains("/chart?symbol=RELIANCE", content);

        // REQ-LEGAL-007 short-form disclaimer
        Assert.Contains("This is not investment advice.", content);
    }

    [Fact]
    public void BuildEntryFillContent_includes_header_with_symbol_and_exchange()
    {
        var data = new EntryFillData(
            Symbol: "HDFCBANK",
            ExchangeCode: "NSE",
            Side: "BUY",
            FilledQuantity: 50,
            RequestedQuantity: null,
            AverageFillPrice: 1680.00m,
            StopLevel: 1596.00m,
            RiskPercent: 0.8m,
            RiskAmount: 4200.00m,
            IntentSource: "Manual",
            DeepLink: "/chart?symbol=HDFCBANK"
        );

        var content = NotificationTemplateBuilder.BuildEntryFillContent(data);

        Assert.Contains("HDFCBANK", content);
        Assert.Contains("NSE", content);
        Assert.Contains("Manual", content);
        Assert.Contains("₹1680.00", content);
        Assert.Contains("This is not investment advice.", content);
    }

    [Fact]
    public void BuildEntryFillContent_handles_partial_fill()
    {
        var data = new EntryFillData(
            Symbol: "TCS",
            ExchangeCode: "NSE",
            Side: "BUY",
            FilledQuantity: 50,
            RequestedQuantity: 100,
            AverageFillPrice: 3890.00m,
            StopLevel: 3695.50m,
            RiskPercent: 1.2m,
            RiskAmount: 2334.00m,
            IntentSource: "Signal: Volume Spike (Daily)",
            DeepLink: "/chart?symbol=TCS"
        );

        var content = NotificationTemplateBuilder.BuildEntryFillContent(data);

        // Partial fill indicator in header
        Assert.Contains("Partial", content);

        // REQ-NOTIFY-024 partial-fill: "Filled: 50 of 100 units requested"
        Assert.Contains("Filled: 50 of 100 units requested", content);

        // All other fields still present
        Assert.Contains("TCS", content);
        Assert.Contains("BUY", content);
        Assert.Contains("₹3890.00", content);
        Assert.Contains("₹3695.50", content);
        Assert.Contains("1.2%", content);
        Assert.Contains("Signal: Volume Spike (Daily)", content);
        Assert.Contains("This is not investment advice.", content);
    }

    [Fact]
    public void BuildEntryFillContent_uses_sell_side_for_exit_actions()
    {
        var data = new EntryFillData(
            Symbol: "INFY",
            ExchangeCode: "NSE",
            Side: "SELL",
            FilledQuantity: 200,
            RequestedQuantity: null,
            AverageFillPrice: 1520.00m,
            StopLevel: 1444.00m,
            RiskPercent: 1.0m,
            RiskAmount: 2000.00m,
            IntentSource: "Manual",
            DeepLink: "/chart?symbol=INFY"
        );

        var content = NotificationTemplateBuilder.BuildEntryFillContent(data);

        Assert.Contains("SELL", content);
    }

    // ── Exit-advisory template (REQ-NOTIFY-025) ───────────────────────────

    [Fact]
    public void BuildExitAdvisoryContent_includes_all_required_fields()
    {
        var data = new ExitAdvisoryData(
            Symbol: "TATAMOTORS",
            ExchangeCode: "NSE",
            ExitReason: "Trailing stop hit",
            ExitPriority: 3,
            CurrentPrice: 620.00m,
            DistanceToStopInr: 15.00m,
            DistanceToStopPercent: 2.4m,
            OpenQuantity: 200,
            EstimatedPnL: 3000.00m,
            EstimatedRMultiples: 2.5m,
            DeepLink: "/chart?symbol=TATAMOTORS"
        );

        var content = NotificationTemplateBuilder.BuildExitAdvisoryContent(data);

        // REQ-NOTIFY-025(a): symbol and exchange code
        Assert.Contains("TATAMOTORS", content);
        Assert.Contains("NSE", content);

        // REQ-NOTIFY-025(b): exit reason + priority rank
        Assert.Contains("Trailing stop hit", content);
        Assert.Contains("Priority 3", content);

        // REQ-NOTIFY-025(c): current market price in INR, 2 decimal places
        Assert.Contains("₹620.00", content);

        // REQ-NOTIFY-025(d): distance to stop in INR and as % of current price
        Assert.Contains("₹15.00", content);
        Assert.Contains("2.4%", content);

        // REQ-NOTIFY-025(e): open quantity
        Assert.Contains("200", content);

        // REQ-NOTIFY-025(f): estimated PnL in INR and R multiples
        Assert.Contains("₹3000.00", content);
        Assert.Contains("2.5 R", content);

        // REQ-NOTIFY-025(g): portal deep link
        Assert.Contains("/chart?symbol=TATAMOTORS", content);

        // REQ-LEGAL-007 short-form disclaimer
        Assert.Contains("This is not investment advice.", content);
    }

    [Fact]
    public void BuildExitAdvisoryContent_formats_negative_pnl_correctly()
    {
        var data = new ExitAdvisoryData(
            Symbol: "WIPRO",
            ExchangeCode: "NSE",
            ExitReason: "Stop loss triggered",
            ExitPriority: 1,
            CurrentPrice: 280.00m,
            DistanceToStopInr: -5.00m,
            DistanceToStopPercent: -1.8m,
            OpenQuantity: 150,
            EstimatedPnL: -1500.00m,
            EstimatedRMultiples: -1.2m,
            DeepLink: "/chart?symbol=WIPRO"
        );

        var content = NotificationTemplateBuilder.BuildExitAdvisoryContent(data);

        Assert.Contains("WIPRO", content);
        Assert.Contains("Stop loss triggered", content);
        Assert.Contains("Priority 1", content);
        Assert.Contains("₹280.00", content);
        Assert.Contains("-1.8%", content);
        Assert.Contains("₹-1500.00", content);
        Assert.Contains("-1.2 R", content);
        Assert.Contains("This is not investment advice.", content);
    }

    [Fact]
    public void BuildExitAdvisoryContent_includes_exit_reason_and_priority()
    {
        var data = new ExitAdvisoryData(
            Symbol: "MARUTI",
            ExchangeCode: "NSE",
            ExitReason: "Gap risk protection",
            ExitPriority: 5,
            CurrentPrice: 10200.00m,
            DistanceToStopInr: 300.00m,
            DistanceToStopPercent: 2.9m,
            OpenQuantity: 10,
            EstimatedPnL: 5000.00m,
            EstimatedRMultiples: 3.0m,
            DeepLink: "/chart?symbol=MARUTI"
        );

        var content = NotificationTemplateBuilder.BuildExitAdvisoryContent(data);

        Assert.Contains("Gap risk protection", content);
        Assert.Contains("Priority 5", content);
        Assert.Contains("₹10200.00", content);
    }

    // ── Null guard ────────────────────────────────────────────────────────

    [Fact]
    public void BuildEntryFillContent_throws_on_null_data()
    {
        Assert.Throws<ArgumentNullException>(
            () => NotificationTemplateBuilder.BuildEntryFillContent(null!));
    }

    [Fact]
    public void BuildExitAdvisoryContent_throws_on_null_data()
    {
        Assert.Throws<ArgumentNullException>(
            () => NotificationTemplateBuilder.BuildExitAdvisoryContent(null!));
    }
}
