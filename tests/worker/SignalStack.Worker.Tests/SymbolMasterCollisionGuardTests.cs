using MongoDB.Bson;
using SignalStack.Worker.Hosting;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="SymbolMasterCollisionGuard.FindCollisions"/>.
/// REQ-HIST-008a.
/// </summary>
public sealed class SymbolMasterCollisionGuardTests
{
    private static BsonDocument MakeDoc(string symbol, string suffix)
    {
        return new BsonDocument
        {
            ["symbol"] = symbol,
            ["sql_table_name_suffix"] = suffix
        };
    }

    [Fact]
    public void Empty_list_returns_no_collisions()
    {
        var result = SymbolMasterCollisionGuard.FindCollisions([]);
        Assert.Empty(result);
    }

    [Fact]
    public void Single_symbol_returns_no_collisions()
    {
        var docs = new List<BsonDocument> { MakeDoc("RELIANCE", "RELIANCE") };
        var result = SymbolMasterCollisionGuard.FindCollisions(docs);
        Assert.Empty(result);
    }

    [Fact]
    public void Unique_suffixes_return_no_collisions()
    {
        var docs = new List<BsonDocument>
        {
            MakeDoc("RELIANCE", "RELIANCE"),
            MakeDoc("TCS", "TCS"),
            MakeDoc("INFY", "INFY")
        };
        var result = SymbolMasterCollisionGuard.FindCollisions(docs);
        Assert.Empty(result);
    }

    [Fact]
    public void Duplicate_suffixes_are_detected()
    {
        var docs = new List<BsonDocument>
        {
            MakeDoc("M&M", "M_M"),
            MakeDoc("M_M", "M_M"),
            MakeDoc("RELIANCE", "RELIANCE")
        };
        var result = SymbolMasterCollisionGuard.FindCollisions(docs);
        Assert.Single(result);
        Assert.Equal("M_M", result[0].Suffix);
        Assert.Contains("M&M", result[0].Symbols);
        Assert.Contains("M_M", result[0].Symbols);
    }

    [Fact]
    public void Multiple_collision_groups_are_all_reported()
    {
        var docs = new List<BsonDocument>
        {
            MakeDoc("A_B", "A_B"),
            MakeDoc("A-B", "A_B"),
            MakeDoc("C_D", "C_D"),
            MakeDoc("C-D", "C_D")
        };
        var result = SymbolMasterCollisionGuard.FindCollisions(docs);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Three_way_collision_is_reported_as_single_group()
    {
        var docs = new List<BsonDocument>
        {
            MakeDoc("A", "SAME"),
            MakeDoc("B", "SAME"),
            MakeDoc("C", "SAME")
        };
        var result = SymbolMasterCollisionGuard.FindCollisions(docs);
        Assert.Single(result);
        Assert.Equal("SAME", result[0].Suffix);
        Assert.Equal(3, result[0].Symbols.Count);
    }

    [Fact]
    public void Collision_symbols_are_ordered_alphabetically()
    {
        var docs = new List<BsonDocument>
        {
            MakeDoc("Z", "SAME"),
            MakeDoc("A", "SAME"),
            MakeDoc("M", "SAME")
        };
        var result = SymbolMasterCollisionGuard.FindCollisions(docs);
        Assert.Equal(["A", "M", "Z"], result[0].Symbols);
    }
}
