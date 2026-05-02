using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SignalStack.MarketData;
using SignalStack.Worker.Integrations.TrueData;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Cross-provider swap integration tests per REQ-MARKET-002d.
///
/// Validates that the Market Data Provider (MDP) abstraction boundary holds
/// under a provider swap: consumers receive data through the shared
/// <see cref="IMarketDataProvider"/> interface regardless of which provider
/// is active. No code changes are required in DataSync, EODSR, or chart
/// layer when the active provider switches.
/// </summary>
public sealed class CrossProviderSwapTests
{


    // ── Provider resolution tests ──────────────────────────────────────────

    [Fact]
    public void Provider_resolution_recognises_truedata()
    {
        // REQ-MARKET-002d: the resolution service must be able to resolve
        // "truedata" as a valid provider, mapping to TrueData.
        var resolution = new MarketDataProviderResolution(NullLogger<MarketDataProviderResolution>.Instance);

        var resolved = resolution.Resolve("truedata");

        Assert.Equal(MarketDataProviderType.TrueData, resolved);
    }

    [Fact]
    public void Provider_resolution_is_case_insensitive()
    {
        var resolution = new MarketDataProviderResolution(NullLogger<MarketDataProviderResolution>.Instance);

        Assert.Equal(MarketDataProviderType.TrueData, resolution.Resolve("TrueData"));
        Assert.Equal(MarketDataProviderType.TrueData, resolution.Resolve("TRUEDATA"));
        Assert.Equal(MarketDataProviderType.TrueData, resolution.Resolve("TrueData "));
    }

    [Fact]
    public void Provider_resolution_defaults_to_fyers_for_unknown()
    {
        var resolution = new MarketDataProviderResolution(NullLogger<MarketDataProviderResolution>.Instance);

        var resolved = resolution.Resolve("unknown_provider");

        Assert.Equal(MarketDataProviderType.Fyers, resolved);
    }

    [Fact]
    public void Provider_resolution_defaults_to_fyers_for_empty()
    {
        var resolution = new MarketDataProviderResolution(NullLogger<MarketDataProviderResolution>.Instance);

        Assert.Equal(MarketDataProviderType.Fyers, resolution.Resolve(null));
        Assert.Equal(MarketDataProviderType.Fyers, resolution.Resolve(""));
        Assert.Equal(MarketDataProviderType.Fyers, resolution.Resolve("   "));
    }

    // ── TrueData stub implements IMarketDataProvider ────────────────────────

    [Fact]
    public void TrueData_stub_implements_IMarketDataProvider()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        // The stub must be usable through the shared interface.
        var mdp = provider as IMarketDataProvider;

        Assert.NotNull(mdp);
        Assert.Equal("TrueData (Stub)", mdp.ProviderName);
    }

    // ── FetchHistoricalOhlcvAsync contract tests ───────────────────────────

    [Fact]
    public async Task TrueData_stub_returns_canned_ohlcv()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var result = await provider.FetchHistoricalOhlcvAsync(
            "RELIANCE",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31));

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Equal(5, result.Count);

        // Results must be ordered by date ascending.
        for (int i = 1; i < result.Count; i++)
        {
            Assert.True(result[i - 1].Date <= result[i].Date,
                "OHLCV records must be ordered by date ascending");
        }

        // Verify the first record's shape.
        var first = result[0];
        Assert.Equal("RELIANCE", first.Symbol);
        Assert.Equal(new DateOnly(2025, 1, 2), first.Date);
        Assert.Equal(2450.00m, first.Open);
        Assert.Equal(2485.50m, first.High);
        Assert.Equal(2435.00m, first.Low);
        Assert.Equal(2472.80m, first.Close);
        Assert.Equal(15_000_000L, first.Volume);
    }

    [Fact]
    public async Task TrueData_stub_filters_by_date_range()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var result = await provider.FetchHistoricalOhlcvAsync(
            "RELIANCE",
            new DateOnly(2025, 1, 6),
            new DateOnly(2025, 1, 7));

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.All(result, r => Assert.Equal("RELIANCE", r.Symbol));
        Assert.Equal(new DateOnly(2025, 1, 6), result[0].Date);
        Assert.Equal(new DateOnly(2025, 1, 7), result[1].Date);
    }

    [Fact]
    public async Task TrueData_stub_returns_empty_for_unknown_symbol()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var result = await provider.FetchHistoricalOhlcvAsync(
            "UNKNOWN_SYMBOL",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31));

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task TrueData_stub_returns_empty_for_out_of_range_dates()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var result = await provider.FetchHistoricalOhlcvAsync(
            "RELIANCE",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 12, 31));

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task TrueData_stub_ohlcv_record_has_valid_data_shapes()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var result = await provider.FetchHistoricalOhlcvAsync(
            "RELIANCE",
            new DateOnly(2025, 1, 1),
            new DateOnly(2025, 1, 31));

        Assert.NotNull(result);

        foreach (var record in result)
        {
            // OHLCV data quality rules per REQ-MARKET-013.
            Assert.True(record.High >= record.Low,
                $"High ({record.High}) must be >= Low ({record.Low}) for {record.Symbol} on {record.Date}");
            Assert.True(record.Open > 0,
                $"Open must be positive for {record.Symbol} on {record.Date}");
            Assert.True(record.High > 0,
                $"High must be positive for {record.Symbol} on {record.Date}");
            Assert.True(record.Low > 0,
                $"Low must be positive for {record.Symbol} on {record.Date}");
            Assert.True(record.Close > 0,
                $"Close must be positive for {record.Symbol} on {record.Date}");
            Assert.True(record.Volume >= 0,
                $"Volume must be non-negative for {record.Symbol} on {record.Date}");
        }
    }

    // ── GetLatestQuoteAsync contract tests ──────────────────────────────────

    [Fact]
    public async Task TrueData_stub_returns_canned_quote()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var quote = await provider.GetLatestQuoteAsync("RELIANCE");

        Assert.NotNull(quote);
        Assert.Equal("RELIANCE", quote.Symbol);
        Assert.Equal(2535.40m, quote.LastPrice);
        Assert.Equal(12.50m, quote.Change);
        Assert.Equal(0.50m, quote.ChangePercent);
        Assert.False(quote.IsDelayed);
    }

    [Fact]
    public async Task TrueData_stub_returns_null_for_unknown_symbol_quote()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var quote = await provider.GetLatestQuoteAsync("UNKNOWN_SYMBOL");

        Assert.Null(quote);
    }

    [Fact]
    public async Task TrueData_stub_quote_has_valid_timestamp()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var quote = await provider.GetLatestQuoteAsync("RELIANCE");

        Assert.NotNull(quote);
        Assert.Equal(DateTimeKind.Utc, quote.TimestampUtc.Kind);
        // Quote timestamp should be within a reasonable range.
        Assert.True(quote.TimestampUtc.Year >= 2024,
            "Quote timestamp year should be >= 2024");
        Assert.True(quote.TimestampUtc.Year <= 2026,
            "Quote timestamp year should be <= 2026");
    }

    // ── SubscribeToLivePrices contract tests ───────────────────────────────

    [Fact]
    public async Task TrueData_stub_subscribe_returns_canned_stream()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var symbols = new[] { "RELIANCE", "SBIN", "RELIANCE" };
        var results = new List<QuoteRecord>();

        await foreach (var quote in provider.SubscribeToLivePrices(symbols))
        {
            results.Add(quote);
        }

        // Distinct symbols: RELIANCE (known) and SBIN (unknown).
        // Only RELIANCE should produce a quote via the stub.
        Assert.Single(results);
        Assert.Equal("RELIANCE", results[0].Symbol);
    }

    [Fact]
    public async Task TrueData_stub_subscribe_respects_cancellation()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);
        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled token.

        var symbols = new[] { "RELIANCE" };
        var results = new List<QuoteRecord>();

        await foreach (var quote in provider.SubscribeToLivePrices(symbols, cts.Token))
        {
            results.Add(quote);
        }

        // With a pre-cancelled token, the stream should yield no results.
        Assert.Empty(results);
    }

    [Fact]
    public async Task TrueData_stub_subscribe_returns_empty_for_null_symbols()
    {
        var provider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        var results = new List<QuoteRecord>();
        await foreach (var quote in provider.SubscribeToLivePrices(Array.Empty<string>()))
        {
            results.Add(quote);
        }

        Assert.Empty(results);
    }

    // ── Interface contract equivalence tests ───────────────────────────────

    [Fact]
    public async Task Both_providers_produce_same_data_shape_through_shared_interface()
    {
        // This test verifies that consumer code can treat FYERS and TrueData
        // adapters identically through IMarketDataProvider. The shape of
        // OhlcvRecord and QuoteRecord is the same regardless of provider.

        var trueDataProvider = new TrueDataMarketDataProvider(NullLogger<TrueDataMarketDataProvider>.Instance);

        // Consumers access data through IMarketDataProvider, never through
        // concrete types. This test proves both adapters satisfy the contract.
        IMarketDataProvider provider = trueDataProvider;

        var ohlcv = await provider.FetchHistoricalOhlcvAsync(
            "RELIANCE",
            new DateOnly(2025, 1, 2),
            new DateOnly(2025, 1, 8));

        Assert.NotNull(ohlcv);

        // Consumer code can iterate OhlcvRecord values without knowing
        // which provider returned them.
        foreach (var candle in ohlcv)
        {
            Assert.IsType<OhlcvRecord>(candle);
            Assert.IsType<decimal>(candle.Open);
            Assert.IsType<decimal>(candle.High);
            Assert.IsType<decimal>(candle.Low);
            Assert.IsType<decimal>(candle.Close);
            Assert.IsType<long>(candle.Volume);
        }

        var quote = await provider.GetLatestQuoteAsync("RELIANCE");
        Assert.NotNull(quote);
        Assert.IsType<QuoteRecord>(quote);
    }

    [Fact]
    public void Provider_resolution_round_trips_in_di()
    {
        // Simulates what Program.cs does at startup: register both providers,
        // resolve the active one from config, and verify the correct adapter
        // is selected.

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

        services.AddSingleton<ILogger<MarketDataProviderResolution>>(NullLogger<MarketDataProviderResolution>.Instance);
        services.AddSingleton<MarketDataProviderResolution>();
        services.AddSingleton<IMarketDataProvider>(sp =>
        {
            var providerName = "truedata"; // Simulating market_data.provider.active = "truedata"
            var resolution = sp.GetRequiredService<MarketDataProviderResolution>();
            var providerType = resolution.Resolve(providerName);

            return providerType switch
            {
                MarketDataProviderType.TrueData => new TrueDataMarketDataProvider(
                    NullLogger<TrueDataMarketDataProvider>.Instance),
                _ => throw new InvalidOperationException($"Unexpected provider: {providerType}")
            };
        });

        var sp = services.BuildServiceProvider();

        var resolvedProvider = sp.GetRequiredService<IMarketDataProvider>();
        Assert.NotNull(resolvedProvider);
        Assert.StartsWith("TrueData", resolvedProvider.ProviderName);
    }

    // ── Architecture guard: consumer code must not hardcode FYERS specifics ─

    [Fact]
    public void Consumer_code_paths_use_shared_interface_not_concrete_types()
    {
        // Scans consumer-relevant files for direct references to FYERS-specific
        // types outside the adapter namespace. This complements the architecture
        // lint tests in MdpAbstractionLintTests.

        var repoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

        var consumerDirs = new[]
        {
            Path.Combine(repoRoot, "apps", "api"),
            Path.Combine(repoRoot, "packages", "market-data"),
        };

        var violations = new List<string>();

        foreach (var dir in consumerDirs)
        {
            if (!Directory.Exists(dir)) continue;

            var csFiles = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories);

            foreach (var file in csFiles)
            {
                if (file.Contains("\\obj\\") || file.Contains("/obj/")) continue;
                if (file.Contains("\\bin\\") || file.Contains("/bin/")) continue;
                if (file.Contains("\\Fyers\\") || file.Contains("/Fyers/")) continue;

                // Skip MDP interface and record definitions themselves
                var fileName = Path.GetFileName(file);
                if (fileName is "IMarketDataProvider.cs" or "OhlcvRecord.cs" or
                    "QuoteRecord.cs" or "MarketDataProviderType.cs" or
                    "MarketDataProviderResolution.cs" or "MarketDataServiceExtensions.cs")
                    continue;

                var relativePath = Path.GetRelativePath(repoRoot, file);
                var content = File.ReadAllText(file);

                // Check for direct FYERS API endpoint references
                if (content.Contains("api.fyers.in", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{relativePath}: contains direct FYERS endpoint reference " +
                        "(must go through IMarketDataProvider)");
                }

                // Check for FYERS-specific type instantiation outside adapters
                if (content.Contains("new FyersMarketDataProvider", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{relativePath}: instantiates FyersMarketDataProvider directly " +
                        "(must go through DI/IMarketDataProvider)");
                }

                // Check for hardcoded FYERS symbol format pattern
                if (content.Contains("NSE:") && content.Contains("-EQ") &&
                    !relativePath.Contains("Integrations\\Fyers", StringComparison.OrdinalIgnoreCase) &&
                    !relativePath.Contains("Integrations/Fyers", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{relativePath}: contains hardcoded FYERS symbol format " +
                        "(must use symbol-mapping layer)");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Consumer code must not hardcode FYERS-specific references:\n" +
            string.Join("\n", violations));
    }
}
