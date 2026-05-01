using System.Text.RegularExpressions;

namespace SignalStack.Api.Universe;

/// <summary>
/// Generates stable SQL table name suffixes from NSE trading symbols.
/// REQ-HIST-005/006/007/008.
/// </summary>
public static partial class SqlTableNameSuffixGenerator
{
    // Matches any character NOT in [A-Z0-9].
    [GeneratedRegex("[^A-Z0-9]")]
    private static partial Regex NonAlphanumericRegex();

    // Collapses consecutive underscores into a single underscore.
    [GeneratedRegex("_+")]
    private static partial Regex CollapseUnderscoresRegex();

    /// <summary>
    /// Generates a deterministic <c>sql_table_name_suffix</c> from a trading symbol
    /// per REQ-HIST-006 and REQ-HIST-007.
    /// </summary>
    /// <param name="symbol">The NSE trading symbol (e.g. "RELIANCE", "M&M").</param>
    /// <returns>The sanitised suffix.</returns>
    /// <exception cref="ArgumentException">Thrown when the cleansed result is empty.</exception>
    public static string Generate(string symbol)
    {
        // (a) uppercase
        var cleansed = symbol.ToUpperInvariant();

        // (b) replace any character not in [A-Z0-9] with _
        cleansed = NonAlphanumericRegex().Replace(cleansed, "_");

        // (c) collapse consecutive underscores
        cleansed = CollapseUnderscoresRegex().Replace(cleansed, "_");

        // (d) strip leading and trailing underscores
        cleansed = cleansed.Trim('_');

        // If empty, reject per REQ-HIST-006
        if (string.IsNullOrEmpty(cleansed))
        {
            throw new ArgumentException(
                $"Symbol '{symbol}' produces an empty sql_table_name_suffix after sanitisation. " +
                "Import must reject this symbol with a clear operator error.",
                nameof(symbol));
        }

        // (e) truncate to 100 characters
        if (cleansed.Length > 100)
        {
            cleansed = cleansed[..100];
        }

        // REQ-HIST-007: if starts with a digit, prefix with SYM_
        if (char.IsAsciiDigit(cleansed[0]))
        {
            cleansed = $"SYM_{cleansed}";
        }

        return cleansed;
    }
}
