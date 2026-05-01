using System.Diagnostics.CodeAnalysis;

namespace SignalStack.Worker.Integrations.Fyers;

/// <summary>
/// Formats plain NSE symbols to FYERS API symbol format.
///
/// FYERS uses <c>NSE:SYMBOL-EQ</c> format for equity symbols traded on NSE.
/// The platform stores symbols in plain form (e.g., "SBIN", "RELIANCE") and
/// the adapter is responsible for formatting them for the provider.
/// </summary>
internal static class FyersSymbolFormatter
{
    /// <summary>
    /// Convert a plain symbol to FYERS API format.
    /// </summary>
    /// <param name="symbol">Plain symbol, e.g., "SBIN".</param>
    /// <returns>FYERS-format symbol, e.g., "NSE:SBIN-EQ".</returns>
    public static string ToFyersFormat(string symbol)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var upper = symbol.Trim().ToUpperInvariant();

        // If already in FYERS format, return as-is.
        if (upper.StartsWith("NSE:", StringComparison.Ordinal))
            return upper;

        // If it already has -EQ suffix, just prefix NSE:
        if (upper.EndsWith("-EQ", StringComparison.Ordinal))
            return $"NSE:{upper}";

        return $"NSE:{upper}-EQ";
    }

    /// <summary>
    /// Attempt to parse a FYERS-format symbol back to plain form.
    /// Used when normalising provider responses.
    /// </summary>
    public static bool TryParsePlainSymbol(string fyersSymbol, [MaybeNullWhen(false)] out string plainSymbol)
    {
        plainSymbol = null;

        if (string.IsNullOrWhiteSpace(fyersSymbol))
            return false;

        var upper = fyersSymbol.Trim().ToUpperInvariant();

        if (!upper.StartsWith("NSE:", StringComparison.Ordinal))
            return false;

        var withoutPrefix = upper[4..]; // Remove "NSE:"

        // Remove -EQ suffix if present
        plainSymbol = withoutPrefix.EndsWith("-EQ", StringComparison.Ordinal)
            ? withoutPrefix[..^3]
            : withoutPrefix;

        return !string.IsNullOrWhiteSpace(plainSymbol);
    }
}
