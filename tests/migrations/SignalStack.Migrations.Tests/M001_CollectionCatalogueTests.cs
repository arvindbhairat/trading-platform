using SignalStack.Migrations;
using SignalStack.Migrations.Migrations;
using Xunit;

namespace SignalStack.Migrations.Tests;

/// <summary>
/// Lint-style tests that verify the M001 collection catalogue matches the prescriptions
/// in docs/data-management.md without requiring a live MongoDB instance.
/// </summary>
public sealed class M001_CollectionCatalogueTests
{
    // Every collection listed in the data-management.md vertical slice.
    private static readonly string[] RequiredCollections =
    [
        "users", "sessions", "fyers_tokens", "symbol_master", "symbol_health",
        "trading_calendar", "sys_config", "watchlists", "signals", "signal_runs",
        "entry_signals", "notifications", "notification_bots", "positions",
        "rme_events", "rme_incidents", "trade_ledger", "intent_ledger",
        "broker_orders", "equity_curve", "portfolio_snapshots", "manual_adjustments",
        "fyers_account_sync", "rme_profile_recommendations", "universe_upload_results",
        "audit_events", "job_runs",
    ];

    // ── Collection completeness ──────────────────────────────────────────────

    [Fact]
    public void Catalogue_contains_exactly_27_prescribed_collections()
    {
        var actual = CollectionCatalogueSpec.All.Select(c => c.Name).ToHashSet();
        Assert.Equal(RequiredCollections.Length, actual.Count);
        foreach (var name in RequiredCollections)
            Assert.True(actual.Contains(name), $"Missing collection: {name}");
    }

    [Theory]
    [MemberData(nameof(GetAllCollectionNames))]
    public void Each_collection_has_a_non_empty_name(string name)
        => Assert.False(string.IsNullOrWhiteSpace(name));

    public static IEnumerable<object[]> GetAllCollectionNames()
        => CollectionCatalogueSpec.All.Select(c => new object[] { c.Name });

    // ── TTL indexes ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("sessions",    "issued_at",    86_400,     false)]   // 24 h
    [InlineData("fyers_tokens","superseded_at", 2_592_000, true)]    // 30 d, sparse
    [InlineData("broker_orders","updated_at",   2_592_000, false)]   // 30 d
    [InlineData("rme_incidents","resolved_at", 15_552_000, true)]    // 6 mo, sparse
    public void TTL_index_has_correct_field_duration_and_sparseness(
        string collection, string field, int expectedSeconds, bool expectedSparse)
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == collection);
        var ttl  = spec.TtlIndexes.Single(t => t.FieldName == field);
        Assert.Equal(expectedSeconds, (int)ttl.Expiry.TotalSeconds);
        Assert.Equal(expectedSparse, ttl.Sparse);
    }

    [Fact]
    public void Exactly_four_collections_have_TTL_indexes()
    {
        var withTtl = CollectionCatalogueSpec.All.Where(c => c.TtlIndexes.Count > 0).ToList();
        Assert.Equal(4, withTtl.Count);
    }

    // ── Online Archive flags ─────────────────────────────────────────────────

    private static readonly string[] OnlineArchiveCollections =
    [
        "signal_runs", "entry_signals", "notifications", "positions",
        "rme_events", "trade_ledger", "intent_ledger", "equity_curve",
        "rme_profile_recommendations", "audit_events", "job_runs",
    ];

    [Theory]
    [MemberData(nameof(GetOnlineArchiveCollections))]
    public void Online_archive_collection_has_flag_and_archive_field(string collection)
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == collection);
        Assert.True(spec.HasOnlineArchive, $"{collection} must have HasOnlineArchive = true");
        Assert.False(string.IsNullOrWhiteSpace(spec.OnlineArchiveField),
            $"{collection} must declare OnlineArchiveField");
    }

    public static IEnumerable<object[]> GetOnlineArchiveCollections()
        => OnlineArchiveCollections.Select(c => new object[] { c });

    [Fact]
    public void Exactly_eleven_collections_are_marked_for_online_archive()
    {
        var count = CollectionCatalogueSpec.All.Count(c => c.HasOnlineArchive);
        Assert.Equal(11, count);
    }

    [Theory]
    [InlineData("signal_runs",               "run_at")]
    [InlineData("entry_signals",             "generated_at")]
    [InlineData("notifications",             "generated_at")]
    [InlineData("positions",                 "closed_at")]
    [InlineData("rme_events",               "event_at")]
    [InlineData("trade_ledger",             "trade_at")]
    [InlineData("intent_ledger",            "created_at")]
    [InlineData("equity_curve",             "session_date")]
    [InlineData("rme_profile_recommendations", "superseded_at")]
    [InlineData("audit_events",             "event_at")]
    [InlineData("job_runs",                 "started_at")]
    public void Online_archive_field_matches_data_management_md(
        string collection, string expectedField)
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == collection);
        Assert.Equal(expectedField, spec.OnlineArchiveField);
    }

    // ── Key unique indexes ────────────────────────────────────────────────────

    [Theory]
    [InlineData("users",             "email")]
    [InlineData("symbol_master",     "symbol")]
    [InlineData("symbol_master",     "isin")]
    [InlineData("symbol_master",     "sql_table_name_suffix")]
    [InlineData("symbol_health",     "symbol")]
    [InlineData("sys_config",        "key")]
    [InlineData("watchlists",        "user_id")]
    [InlineData("intent_ledger",     "nonce")]
    [InlineData("portfolio_snapshots","user_id")]
    [InlineData("fyers_account_sync","user_id")]
    public void Unique_single_field_index_is_declared(string collection, string field)
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == collection);
        Assert.Contains(spec.SingleFieldIndexes, i => i.FieldName == field && i.Unique);
    }

    [Fact]
    public void Trading_calendar_has_unique_compound_index_on_session_date_and_type()
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == "trading_calendar");
        Assert.Contains(spec.CompoundIndexes, ci =>
            ci.Unique &&
            ci.Fields.Any(f => f.Field == "session_date") &&
            ci.Fields.Any(f => f.Field == "session_type"));
    }

    [Fact]
    public void TradeLedger_has_unique_compound_index_on_user_id_and_trade_id()
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == "trade_ledger");
        Assert.Contains(spec.CompoundIndexes, ci =>
            ci.Unique &&
            ci.Fields.Any(f => f.Field == "user_id") &&
            ci.Fields.Any(f => f.Field == "trade_id"));
    }

    // ── rme_incidents compound indexes (data-management.md explicit prescriptions) ──

    [Fact]
    public void RmeIncidents_has_status_created_at_compound_index()
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == "rme_incidents");
        Assert.Contains(spec.CompoundIndexes, ci =>
            ci.Fields.Any(f => f.Field == "status") &&
            ci.Fields.Any(f => f.Field == "created_at"));
    }

    [Fact]
    public void RmeIncidents_has_position_id_incident_type_status_compound_index()
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == "rme_incidents");
        Assert.Contains(spec.CompoundIndexes, ci =>
            ci.Fields.Any(f => f.Field == "position_id") &&
            ci.Fields.Any(f => f.Field == "incident_type") &&
            ci.Fields.Any(f => f.Field == "status"));
    }

    // ── intent_ledger indexes (data-management.md explicit prescriptions) ────

    [Fact]
    public void IntentLedger_has_user_status_created_at_compound_index()
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == "intent_ledger");
        Assert.Contains(spec.CompoundIndexes, ci =>
            ci.Fields.Any(f => f.Field == "user_id") &&
            ci.Fields.Any(f => f.Field == "status") &&
            ci.Fields.Any(f => f.Field == "created_at"));
    }

    // ── Positions — notes from data-management.md ────────────────────────────

    [Fact]
    public void Positions_closed_at_index_is_sparse_so_open_positions_are_excluded_from_archive()
    {
        var spec = CollectionCatalogueSpec.All.Single(c => c.Name == "positions");
        Assert.Contains(spec.SingleFieldIndexes, i => i.FieldName == "closed_at" && i.Sparse);
    }

    // ── Atlas Online Archive policy document ─────────────────────────────────

    [Fact]
    public void Atlas_online_archive_policy_document_exists()
    {
        var path = GetPolicyPath();
        Assert.True(File.Exists(path), $"Atlas Online Archive policy file not found at {path}");
    }

    [Theory]
    [MemberData(nameof(GetOnlineArchiveCollections))]
    public void Atlas_online_archive_policy_document_covers_collection(string collection)
    {
        var json = File.ReadAllText(GetPolicyPath());
        Assert.Contains($"\"collection\": \"{collection}\"", json);
    }

    // ── Migration identity ────────────────────────────────────────────────────

    [Fact]
    public void M001_migration_id_is_M001()
    {
        var migration = new M001_InitialCollectionCatalogue();
        Assert.Equal("M001", migration.Id);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string GetPolicyPath()
    {
        var repoRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        return Path.Combine(
            repoRoot, "packages", "migrations", "SignalStack.Migrations",
            "atlas-online-archive-policies.json");
    }
}
