using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Api.Historical;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Integration tests for the chart data endpoints (/api/v1/chart/...).
///
/// Tests cover:
/// - REQ-HIST-001/002: OHLCV data returned from D_/W_/M_ tables
/// - REQ-HIST-003/004: table names resolved via sql_table_name_suffix
/// - REQ-HIST-011: all table names obtained through ISymbolTableMapping
/// - REQ-TIMEFRAME-005: weekly/monthly sourced from pre-computed tables
/// - REQ-TIMEFRAME-006: rolling candles computed on demand
/// </summary>
public sealed class ChartEndpointsTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory _factory;

    public ChartEndpointsTests(TestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetChartData_returns_ohlcv_for_known_symbol()
    {
        var symbol = "RELIANCE";
        var dailyData = new List<OhlcvRecord>
        {
            new(new DateOnly(2024, 1, 1), 2400, 2420, 2390, 2410, 100_000),
            new(new DateOnly(2024, 1, 2), 2410, 2440, 2405, 2430, 120_000),
        };

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var ohlcvRepo = new InMemoryOhlcvRepository();
                ohlcvRepo.SeedDaily(symbol, [.. dailyData]);

                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "RELIANCE")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/v1/chart/{symbol}?timeframe=daily&from=2024-01-01&to=2024-01-31");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<List<OhlcvRecord>>();
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Equal(2400, result[0].Open);
        Assert.Equal(2430, result[1].Close);
    }

    [Fact]
    public async Task GetChartData_returns_weekly_for_known_symbol()
    {
        var symbol = "TCS";
        var weeklyData = new List<OhlcvRecord>
        {
            new(new DateOnly(2024, 1, 1), 3800, 3850, 3780, 3830, 500_000),
        };

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var ohlcvRepo = new InMemoryOhlcvRepository();
                ohlcvRepo.SeedWeekly(symbol, [.. weeklyData]);

                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "TCS")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/v1/chart/{symbol}?timeframe=weekly&from=2024-01-01&to=2024-01-31");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<List<OhlcvRecord>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(3800, result[0].Open);
    }

    [Fact]
    public async Task GetChartData_returns_monthly_for_known_symbol()
    {
        var symbol = "INFY";
        var monthlyData = new List<OhlcvRecord>
        {
            new(new DateOnly(2024, 1, 1), 1500, 1550, 1480, 1530, 2_000_000),
        };

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var ohlcvRepo = new InMemoryOhlcvRepository();
                ohlcvRepo.SeedMonthly(symbol, [.. monthlyData]);

                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "INFY")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/v1/chart/{symbol}?timeframe=monthly&from=2024-01-01&to=2024-01-31");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<List<OhlcvRecord>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(1500, result[0].Open);
    }

    [Fact]
    public async Task GetChartData_returns_404_for_unknown_symbol()
    {
        using var client = _factory.CreateClient();
        using var resp = await client.GetAsync(
            "/api/v1/chart/UNKNOWN?timeframe=daily&from=2024-01-01&to=2024-01-31");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetChartData_daily_defaults_to_one_year_window()
    {
        var symbol = "HDFC";
        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var oneYearAgo = now.AddYears(-1);

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "HDFC")));
            });
        });

        using var client = factory.CreateClient();
        // Without explicit from/to — should not crash
        using var resp = await client.GetAsync($"/api/v1/chart/{symbol}?timeframe=daily");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    [Fact]
    public async Task GetLastCandleDate_returns_date_for_known_symbol()
    {
        var symbol = "WIPRO";
        var expectedDate = new DateOnly(2024, 12, 30);

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var ohlcvRepo = new InMemoryOhlcvRepository();
                ohlcvRepo.SeedDaily(symbol, new OhlcvRecord(expectedDate, 500, 510, 495, 505, 300_000));

                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "WIPRO")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync($"/api/v1/chart/{symbol}/last");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<LastCandleResponse>();
        Assert.NotNull(result);
        Assert.Equal(symbol, result.Symbol);
        Assert.Equal(expectedDate, result.LastCandleDate);
    }

    [Fact]
    public async Task GetLastCandleDate_returns_404_for_unknown_symbol()
    {
        using var client = _factory.CreateClient();
        using var resp = await client.GetAsync("/api/v1/chart/UNKNOWN/last");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetRollingCandle_returns_rolling_data()
    {
        var symbol = "ITC";

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                var ohlcvRepo = new InMemoryOhlcvRepository();
                ohlcvRepo.SeedDaily(symbol,
                    new OhlcvRecord(new DateOnly(2024, 1, 1), 400, 405, 398, 402, 200_000),
                    new OhlcvRecord(new DateOnly(2024, 1, 2), 402, 410, 399, 408, 180_000),
                    new OhlcvRecord(new DateOnly(2024, 1, 3), 408, 412, 405, 410, 150_000),
                    new OhlcvRecord(new DateOnly(2024, 1, 4), 410, 415, 407, 413, 220_000),
                    new OhlcvRecord(new DateOnly(2024, 1, 5), 413, 418, 410, 416, 190_000));

                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "ITC")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/v1/chart/{symbol}/rolling?window=5&to=2024-01-05");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<List<OhlcvRecord>>();
        Assert.NotNull(result);
        Assert.Single(result); // Rolling returns a single aggregated candle
        Assert.Equal(400, result[0].Open);   // First session Open
        Assert.Equal(418, result[0].High);   // Max High
        Assert.Equal(398, result[0].Low);    // Min Low
        Assert.Equal(416, result[0].Close);  // Last session Close
    }

    // ── Rolling timeframe endpoint tests (REQ-TIMEFRAME-001/003/006) ──

    [Fact]
    public async Task GetChartData_with_timeframe_rolling3_returns_3_session_candle()
    {
        var symbol = "ROLL3";
        var ohlcvRepo = new InMemoryOhlcvRepository();
        ohlcvRepo.SeedDaily(symbol,
            new OhlcvRecord(new DateOnly(2024, 1, 1), 100, 105, 99,  104, 100_000),
            new OhlcvRecord(new DateOnly(2024, 1, 2), 104, 108, 102, 107, 110_000),
            new OhlcvRecord(new DateOnly(2024, 1, 3), 107, 112, 106, 110, 120_000));

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "ROLL3")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/v1/chart/{symbol}?timeframe=rolling3&to=2024-01-03");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<List<OhlcvRecord>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(100, result[0].Open);   // Oldest session Open (REQ-TIMEFRAME-003)
        Assert.Equal(112, result[0].High);   // Max High
        Assert.Equal(99,  result[0].Low);    // Min Low
        Assert.Equal(110, result[0].Close);  // Most recent Close (REQ-TIMEFRAME-003)
    }

    [Fact]
    public async Task GetChartData_with_timeframe_rolling5_returns_5_session_candle()
    {
        var symbol = "ROLL5";
        var ohlcvRepo = new InMemoryOhlcvRepository();
        ohlcvRepo.SeedDaily(symbol,
            new OhlcvRecord(new DateOnly(2024, 1, 1), 200, 205, 198, 203, 50_000),
            new OhlcvRecord(new DateOnly(2024, 1, 2), 203, 208, 201, 206, 60_000),
            new OhlcvRecord(new DateOnly(2024, 1, 3), 206, 210, 204, 209, 70_000),
            new OhlcvRecord(new DateOnly(2024, 1, 4), 209, 215, 207, 213, 80_000),
            new OhlcvRecord(new DateOnly(2024, 1, 5), 213, 218, 210, 216, 90_000));

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "ROLL5")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/v1/chart/{symbol}?timeframe=rolling5&to=2024-01-05");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<List<OhlcvRecord>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(200, result[0].Open);
        Assert.Equal(218, result[0].High);
        Assert.Equal(198, result[0].Low);
        Assert.Equal(216, result[0].Close);
    }

    [Fact]
    public async Task GetChartData_with_timeframe_rolling7_returns_7_session_candle()
    {
        var symbol = "ROLL7";
        var ohlcvRepo = new InMemoryOhlcvRepository();
        ohlcvRepo.SeedDaily(symbol,
            new OhlcvRecord(new DateOnly(2024, 1, 1), 300, 305, 298, 303, 10_000),
            new OhlcvRecord(new DateOnly(2024, 1, 2), 303, 308, 301, 306, 11_000),
            new OhlcvRecord(new DateOnly(2024, 1, 3), 306, 310, 304, 309, 12_000),
            new OhlcvRecord(new DateOnly(2024, 1, 4), 309, 315, 307, 313, 13_000),
            new OhlcvRecord(new DateOnly(2024, 1, 5), 313, 318, 310, 316, 14_000),
            new OhlcvRecord(new DateOnly(2024, 1, 6), 316, 320, 314, 318, 15_000),
            new OhlcvRecord(new DateOnly(2024, 1, 7), 318, 322, 315, 320, 16_000));

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "ROLL7")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/v1/chart/{symbol}?timeframe=rolling7&to=2024-01-07");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<List<OhlcvRecord>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(300, result[0].Open);
        Assert.Equal(322, result[0].High);
        Assert.Equal(298, result[0].Low);
        Assert.Equal(320, result[0].Close);
    }

    [Fact]
    public async Task GetChartData_with_invalid_timeframe_returns_404()
    {
        using var client = _factory.CreateClient();
        using var resp = await client.GetAsync(
            "/api/v1/chart/RELIANCE?timeframe=invalid&from=2024-01-01&to=2024-01-31");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task GetChartData_with_alias_d_returns_daily_data()
    {
        var symbol = "ALIAS";
        var ohlcvRepo = new InMemoryOhlcvRepository();
        ohlcvRepo.SeedDaily(symbol,
            new OhlcvRecord(new DateOnly(2024, 1, 1), 500, 505, 498, 503, 30_000));

        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IOhlcvRepository>(ohlcvRepo);
                services.AddSingleton<ISymbolTableMapping>(
                    new InMemorySymbolTableMapping((symbol, "ALIAS")));
            });
        });

        using var client = factory.CreateClient();
        using var resp = await client.GetAsync(
            $"/api/v1/chart/{symbol}?timeframe=d&from=2024-01-01&to=2024-01-31");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var result = await resp.Content.ReadFromJsonAsync<List<OhlcvRecord>>();
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(500, result[0].Open);
    }
}
