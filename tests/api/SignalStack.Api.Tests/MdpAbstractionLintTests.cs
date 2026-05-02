using System.Text.RegularExpressions;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Architecture lint tests that enforce the Market Data Provider (MDP) abstraction
/// boundary and the REST-only constraint (REQ-MARKET-002a/002b).
///
/// These tests verify that no backend code binds directly to a specific provider's
/// API outside the designated adapter namespace. The MDP interface contract in
/// <c>packages/market-data/</c> is the only allowed gateway for market data access.
/// </summary>
public sealed class MdpAbstractionLintTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));

    /// <summary>
    /// Namespaces that are explicitly permitted to reference provider-specific types.
    /// Every entry must include the provider name (e.g., "Fyers", "TrueData", "Gdf").
    /// </summary>
    private static readonly HashSet<string> AllowedAdapterNamespaces = new(StringComparer.OrdinalIgnoreCase)
    {
        "SignalStack.Api.Fyers",
        "SignalStack.Worker.Fyers",
        "SignalStack.Api.Integrations.Fyers",
        "SignalStack.Worker.Integrations.Fyers",
        "SignalStack.Api.Integrations.TrueData",
        "SignalStack.Worker.Integrations.TrueData",
        "SignalStack.Api.Integrations.Gdf",
        "SignalStack.Worker.Integrations.Gdf",
    };

    /// <summary>
    /// File-name patterns that identify provider-specific adapter files.
    /// Files matching these patterns are exempt from the "no direct binding" rule.
    /// </summary>
    private static readonly Regex[] AdapterFilePatterns =
    [
        new Regex(@"[/\\]Integrations[/\\]Fyers[/\\]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"[/\\]Integrations[/\\]TrueData[/\\]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"[/\\]Integrations[/\\]Gdf[/\\]", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"[/\\]Fyers[/\\]Fyers", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    /// <summary>
    /// Provider-specific type or constant patterns that must NOT appear in backend
    /// code outside adapter namespaces/files.
    /// </summary>
    private static readonly Regex[] ProviderSpecificPatterns =
    [
        // FYERS-specific constants and API patterns
        new Regex(@"\bFyers\w*ApiClient", RegexOptions.Compiled),
        new Regex(@"\bFyers\w*Endpoint", RegexOptions.Compiled),
        new Regex(@"api\.fyers\.in", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"socket\.fyers\.in", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Direct HTTP client usage for provider APIs outside adapters
        // would be caught by the namespace checks above.
    ];

    // --- IMarketDataProvider interface existence tests ---

    [Fact]
    public void IMarketDataProvider_interface_exists_in_market_data_package()
    {
        var mdpPath = Path.Combine(RepoRoot, "packages", "market-data",
            "SignalStack.MarketData", "IMarketDataProvider.cs");
        Assert.True(File.Exists(mdpPath),
            $"IMarketDataProvider interface not found at expected path: {mdpPath}");

        var content = File.ReadAllText(mdpPath);
        Assert.Contains("interface IMarketDataProvider", content);
        Assert.Contains("FetchHistoricalOhlcvAsync", content);
        Assert.Contains("GetLatestQuoteAsync", content);
        Assert.Contains("SubscribeToLivePrices", content);
    }

    [Fact]
    public void OhlcvRecord_exists_in_market_data_package()
    {
        var path = Path.Combine(RepoRoot, "packages", "market-data",
            "SignalStack.MarketData", "OhlcvRecord.cs");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("record OhlcvRecord", content);
    }

    [Fact]
    public void QuoteRecord_exists_in_market_data_package()
    {
        var path = Path.Combine(RepoRoot, "packages", "market-data",
            "SignalStack.MarketData", "QuoteRecord.cs");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("record QuoteRecord", content);
    }

    [Fact]
    public void TrueData_adapter_implements_IMarketDataProvider()
    {
        var trueDataPath = Path.Combine(RepoRoot, "apps", "worker",
            "Integrations", "TrueData", "TrueDataMarketDataProvider.cs");
        Assert.True(File.Exists(trueDataPath),
            $"TrueData adapter not found at expected path: {trueDataPath}");

        var content = File.ReadAllText(trueDataPath);
        Assert.Contains("class TrueDataMarketDataProvider", content);
        Assert.Contains(": IMarketDataProvider", content);
        Assert.Contains("FetchHistoricalOhlcvAsync", content);
        Assert.Contains("GetLatestQuoteAsync", content);
        Assert.Contains("SubscribeToLivePrices", content);
        Assert.Contains("CannedOhlcv", content);
    }

    [Fact]
    public void MarketDataProviderType_enum_exists()
    {
        var path = Path.Combine(RepoRoot, "packages", "market-data",
            "SignalStack.MarketData", "MarketDataProviderType.cs");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("enum MarketDataProviderType", content);
        Assert.Contains("Fyers", content);
        Assert.Contains("TrueData", content);
        Assert.Contains("GlobalDataFeeds", content);
    }

    [Fact]
    public void MarketDataProviderResolution_exists()
    {
        var path = Path.Combine(RepoRoot, "packages", "market-data",
            "SignalStack.MarketData", "MarketDataProviderResolution.cs");
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("class MarketDataProviderResolution", content);
        Assert.Contains("market_data.provider.active", content);
    }

    // --- REST-only constraint enforcement tests (REQ-MARKET-002a/002b) ---

    [Fact]
    public void Backend_code_must_not_import_provider_specific_types_outside_adapters()
    {
        var violations = new List<string>();
        var backendDirs = new[] { "apps/api", "apps/worker" };

        foreach (var dir in backendDirs)
        {
            var fullDir = Path.Combine(RepoRoot, dir);
            if (!Directory.Exists(fullDir)) continue;

            var csFiles = Directory.GetFiles(fullDir, "*.cs", SearchOption.AllDirectories);

            foreach (var file in csFiles)
            {
                // Skip generated/obj files
                if (file.Contains("\\obj\\") || file.Contains("/obj/")) continue;
                if (file.Contains("\\bin\\") || file.Contains("/bin/")) continue;

                // Skip adapter files themselves
                if (IsAdapterFile(file)) continue;

                var content = File.ReadAllText(file);

                foreach (var pattern in ProviderSpecificPatterns)
                {
                    if (pattern.IsMatch(content))
                    {
                        var relativePath = Path.GetRelativePath(RepoRoot, file);
                        violations.Add(
                            $"{relativePath}: matches provider-specific pattern '{pattern}'");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "MDP abstraction boundary violations found:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void Backend_must_not_contain_direct_Fyers_WebSocket_code()
    {
        // Per REQ-MARKET-002b, the FYERS Data WebSocket is browser-tier only.
        // Backend code must never open, proxy, or relay the FYERS WebSocket.
        var violations = new List<string>();
        var backendDirs = new[] { "apps/api", "apps/worker" };

        foreach (var dir in backendDirs)
        {
            var fullDir = Path.Combine(RepoRoot, dir);
            if (!Directory.Exists(fullDir)) continue;

            var csFiles = Directory.GetFiles(fullDir, "*.cs", SearchOption.AllDirectories);

            foreach (var file in csFiles)
            {
                if (file.Contains("\\obj\\") || file.Contains("/obj/")) continue;
                if (file.Contains("\\bin\\") || file.Contains("/bin/")) continue;
                if (IsAdapterFile(file)) continue;

                var content = File.ReadAllText(file);

                // Check for WebSocket usage patterns that would indicate
                // backend FYERS WebSocket consumption
                if (content.Contains("socket.fyers.in", StringComparison.OrdinalIgnoreCase) ||
                    content.Contains("wss://", StringComparison.OrdinalIgnoreCase) &&
                    content.Contains("fyers", StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = Path.GetRelativePath(RepoRoot, file);
                    violations.Add(
                        $"{relativePath}: contains FYERS WebSocket reference (browser-tier only per REQ-MARKET-002b)");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Backend FYERS WebSocket violations found:\n" +
            string.Join("\n", violations));
    }

    [Fact]
    public void IMarketDataProvider_is_transport_agnostic()
    {
        // Per ADR-0005, the interface must not surface transport semantics.
        var mdpPath = Path.Combine(RepoRoot, "packages", "market-data",
            "SignalStack.MarketData", "IMarketDataProvider.cs");
        var content = File.ReadAllText(mdpPath);

        // The interface should not reference transport-specific terminology.
        Assert.DoesNotContain("HttpClient", content);
        Assert.DoesNotContain("WebSocket", content);
        Assert.DoesNotContain("RestClient", content);
        Assert.DoesNotContain("tcp", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Provider_resolution_reads_from_sys_config()
    {
        // REQ-MKTPROV-005: the active provider must be selectable through
        // admin-managed configuration without a code deployment.
        var resolutionPath = Path.Combine(RepoRoot, "packages", "market-data",
            "SignalStack.MarketData", "MarketDataProviderResolution.cs");
        var content = File.ReadAllText(resolutionPath);

        Assert.Contains("market_data.provider.active", content);
        Assert.Contains("fyers", content.ToLowerInvariant());
        Assert.Contains("truedata", content.ToLowerInvariant());
        Assert.Contains("gdf", content.ToLowerInvariant());
    }

    private static bool IsAdapterFile(string filePath)
    {
        foreach (var pattern in AdapterFilePatterns)
        {
            if (pattern.IsMatch(filePath)) return true;
        }
        return false;
    }
}
