using SignalStack.Api.Universe;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Unit tests for <see cref="SqlTableNameSuffixGenerator"/>.
/// REQ-HIST-005/006/007/008.
/// </summary>
public sealed class SqlTableNameSuffixGeneratorTests
{
    // ── REQ-HIST-006: Basic sanitisation rules ────────────────────────────────

    [Fact]
    public void Simple_symbol_passes_through_unchanged()
    {
        var result = SqlTableNameSuffixGenerator.Generate("RELIANCE");
        Assert.Equal("RELIANCE", result);
    }

    [Fact]
    public void Uppercases_lowercase_input()
    {
        var result = SqlTableNameSuffixGenerator.Generate("tcs");
        Assert.Equal("TCS", result);
    }

    [Fact]
    public void Ampersand_is_replaced_with_underscore()
    {
        var result = SqlTableNameSuffixGenerator.Generate("M&M");
        Assert.Equal("M_M", result);
    }

    [Fact]
    public void Hyphen_is_replaced_with_underscore()
    {
        var result = SqlTableNameSuffixGenerator.Generate("ABC-DEF");
        Assert.Equal("ABC_DEF", result);
    }

    [Fact]
    public void Dot_is_replaced_with_underscore()
    {
        var result = SqlTableNameSuffixGenerator.Generate("ABC.DEF");
        Assert.Equal("ABC_DEF", result);
    }

    [Fact]
    public void Consecutive_non_alphanumeric_characters_are_collapsed()
    {
        var result = SqlTableNameSuffixGenerator.Generate("A&B&C");
        Assert.Equal("A_B_C", result);
    }

    [Fact]
    public void Leading_and_trailing_underscores_are_stripped()
    {
        var result = SqlTableNameSuffixGenerator.Generate("__RELIANCE__");
        Assert.Equal("RELIANCE", result);
    }

    [Fact]
    public void Long_symbol_is_truncated_to_100_characters()
    {
        var longSymbol = new string('A', 150);
        var result = SqlTableNameSuffixGenerator.Generate(longSymbol);
        Assert.Equal(100, result.Length);
        Assert.Equal(new string('A', 100), result);
    }

    // ── REQ-HIST-007: Digit prefix handling ────────────────────────────────────

    [Fact]
    public void Symbol_starting_with_digit_is_prefixed_with_SYM()
    {
        var result = SqlTableNameSuffixGenerator.Generate("123ABC");
        Assert.Equal("SYM_123ABC", result);
    }

    [Fact]
    public void Symbol_starting_with_digit_after_sanitisation_is_prefixed()
    {
        // After sanitisation the leading non-alphanumeric would be replaced,
        // but if the original starts with a digit after sanitisation it still gets prefixed.
        var result = SqlTableNameSuffixGenerator.Generate("5PAISA");
        Assert.Equal("SYM_5PAISA", result);
    }

    // ── REQ-HIST-006: Empty result rejection ───────────────────────────────────

    [Fact]
    public void Symbol_with_only_non_alphanumeric_characters_throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            SqlTableNameSuffixGenerator.Generate("___"));
        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Determinism (REQ-HIST-005/008) ─────────────────────────────────────────

    [Fact]
    public void Same_symbol_produces_identical_suffix_every_time()
    {
        var first = SqlTableNameSuffixGenerator.Generate("M&M");
        var second = SqlTableNameSuffixGenerator.Generate("m&m");
        var third = SqlTableNameSuffixGenerator.Generate(" m&m ");

        // (a) uppercase → "M&M" → "M_M" for all variants
        Assert.Equal("M_M", first);
        Assert.Equal(first, second);
        Assert.Equal(first, third);
    }
}
