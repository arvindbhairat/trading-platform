namespace SignalStack.Migrations;

/// <summary>
/// A TTL index that expires documents after <see cref="Expiry"/> from <see cref="FieldName"/>.
/// Sparse=true means only documents where the field exists are considered (natural MongoDB TTL behaviour
/// for missing fields applies regardless, but we mark it here for operator clarity).
/// </summary>
public sealed record TtlIndexSpec(string FieldName, TimeSpan Expiry, bool Sparse = false);

/// <summary>A single-field ascending or descending index.</summary>
/// <param name="Order">1 = ascending, -1 = descending.</param>
public sealed record SingleFieldIndexSpec(
    string FieldName,
    int Order = 1,
    bool Unique = false,
    bool Sparse = false);

/// <summary>A compound index over two or more fields.</summary>
public sealed record CompoundIndexSpec(
    IReadOnlyList<(string Field, int Order)> Fields,
    bool Unique = false,
    bool Sparse = false);

/// <summary>
/// Full definition of one MongoDB collection as required by <c>docs/data-management.md</c>.
/// </summary>
public sealed record CollectionSpec(
    string Name,
    IReadOnlyList<TtlIndexSpec> TtlIndexes,
    IReadOnlyList<SingleFieldIndexSpec> SingleFieldIndexes,
    IReadOnlyList<CompoundIndexSpec> CompoundIndexes,
    bool HasOnlineArchive = false,
    string? OnlineArchiveField = null);

/// <summary>
/// Canonical catalogue of every MongoDB collection required by the platform.
/// This is the single source of truth consumed by both the M001 migration and the
/// migration lint tests.  Schema changes flow through REQ-MIGRATION-001/004.
/// </summary>
public static class CollectionCatalogueSpec
{
    public static IReadOnlyList<CollectionSpec> All { get; } = Build();

    private static List<CollectionSpec> Build() =>
    [
        // ── users ────────────────────────────────────────────────────────────
        new CollectionSpec(
            Name: "users",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("email",  Unique: true),
                new("status"),
            ],
            CompoundIndexes: []),

        // ── sessions — TTL 24 h on issued_at (REQ-SESSION-001/002) ────────
        new CollectionSpec(
            Name: "sessions",
            TtlIndexes:
            [
                new TtlIndexSpec("issued_at", TimeSpan.FromSeconds(86_400)),
            ],
            SingleFieldIndexes:
            [
                new("user_id"),
                new("expires_at"),
            ],
            CompoundIndexes: []),

        // ── fyers_tokens — TTL 30 d on superseded_at (sparse) ────────────
        new CollectionSpec(
            Name: "fyers_tokens",
            TtlIndexes:
            [
                new TtlIndexSpec("superseded_at", TimeSpan.FromSeconds(2_592_000), Sparse: true),
            ],
            SingleFieldIndexes:
            [
                new("user_id"),
            ],
            CompoundIndexes: []),

        // ── symbol_master ────────────────────────────────────────────────────
        new CollectionSpec(
            Name: "symbol_master",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("symbol",               Unique: true),
                new("isin",                 Unique: true),
                new("sql_table_name_suffix", Unique: true),
                new("is_archived"),
            ],
            CompoundIndexes: []),

        // ── symbol_health ────────────────────────────────────────────────────
        new CollectionSpec(
            Name: "symbol_health",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("symbol", Unique: true),
                new("status"),
            ],
            CompoundIndexes: []),

        // ── trading_calendar ─────────────────────────────────────────────────
        new CollectionSpec(
            Name: "trading_calendar",
            TtlIndexes: [],
            SingleFieldIndexes: [],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("session_date", 1), ("session_type", 1)], Unique: true),
            ]),

        // ── sys_config ───────────────────────────────────────────────────────
        new CollectionSpec(
            Name: "sys_config",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("key",      Unique: true),
                new("category"),
            ],
            CompoundIndexes: []),

        // ── watchlists ───────────────────────────────────────────────────────
        new CollectionSpec(
            Name: "watchlists",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("user_id", Unique: true),
            ],
            CompoundIndexes: []),

        // ── signals ──────────────────────────────────────────────────────────
        new CollectionSpec(
            Name: "signals",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("signal_type"),
                new("is_enabled"),
            ],
            CompoundIndexes: []),

        // ── signal_runs — Online Archive: run_at > 90 d ──────────────────
        new CollectionSpec(
            Name: "signal_runs",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("run_at"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("signal_id", 1), ("run_at", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "run_at"),

        // ── entry_signals — Online Archive: generated_at > 12 mo ─────────
        new CollectionSpec(
            Name: "entry_signals",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("status"),
                new("generated_at"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("symbol", 1), ("signal_type", 1), ("generated_at", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "generated_at"),

        // ── notifications — Online Archive: generated_at > 90 d ──────────
        new CollectionSpec(
            Name: "notifications",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("generated_at"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("user_id", 1), ("status", 1), ("generated_at", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "generated_at"),

        // ── notification_bots ────────────────────────────────────────────────
        new CollectionSpec(
            Name: "notification_bots",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("is_active"),
            ],
            CompoundIndexes: []),

        // ── positions — Online Archive: closed_at > 12 mo (sparse) ───────
        new CollectionSpec(
            Name: "positions",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("closed_at", Sparse: true),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("user_id", 1), ("status", 1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "closed_at"),

        // ── rme_events — Online Archive: event_at > 24 mo ────────────────
        new CollectionSpec(
            Name: "rme_events",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("event_at"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("position_id", 1), ("event_at", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "event_at"),

        // ── rme_incidents — TTL 6 mo on resolved_at (sparse) ─────────────
        new CollectionSpec(
            Name: "rme_incidents",
            TtlIndexes:
            [
                new TtlIndexSpec("resolved_at", TimeSpan.FromSeconds(15_552_000), Sparse: true),
            ],
            SingleFieldIndexes: [],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("status", 1), ("created_at", -1)]),
                new CompoundIndexSpec([("position_id", 1), ("incident_type", 1), ("status", 1)]),
            ]),

        // ── trade_ledger — Online Archive: trade_at > 36 mo ──────────────
        new CollectionSpec(
            Name: "trade_ledger",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("trade_id", Unique: true),
                new("trade_at"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("user_id", 1), ("trade_at", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "trade_at"),

        // ── intent_ledger — Online Archive: created_at > 12 mo ───────────
        new CollectionSpec(
            Name: "intent_ledger",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("nonce",      Unique: true),
                new("created_at"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("user_id", 1), ("status", 1), ("created_at", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "created_at"),

        // ── broker_orders — TTL 30 d on updated_at ───────────────────────
        new CollectionSpec(
            Name: "broker_orders",
            TtlIndexes:
            [
                new TtlIndexSpec("updated_at", TimeSpan.FromSeconds(2_592_000)),
            ],
            SingleFieldIndexes: [],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("user_id", 1), ("status", 1)]),
            ]),

        // ── equity_curve — Online Archive: session_date > 36 mo ──────────
        new CollectionSpec(
            Name: "equity_curve",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("session_date"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("user_id", 1), ("session_date", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "session_date"),

        // ── portfolio_snapshots — one per user, updated in place ─────────
        new CollectionSpec(
            Name: "portfolio_snapshots",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("user_id", Unique: true),
            ],
            CompoundIndexes: []),

        // ── manual_adjustments ───────────────────────────────────────────
        new CollectionSpec(
            Name: "manual_adjustments",
            TtlIndexes: [],
            SingleFieldIndexes: [],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("user_id", 1), ("created_at", -1)]),
            ]),

        // ── fyers_account_sync — one per user, updated in place ──────────
        new CollectionSpec(
            Name: "fyers_account_sync",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("user_id", Unique: true),
            ],
            CompoundIndexes: []),

        // ── rme_profile_recommendations — Online Archive: superseded_at > 12 mo
        new CollectionSpec(
            Name: "rme_profile_recommendations",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("superseded_at", Sparse: true),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("symbol", 1), ("signal_subscription_id", 1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "superseded_at"),

        // ── universe_upload_results ──────────────────────────────────────
        new CollectionSpec(
            Name: "universe_upload_results",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("uploaded_at"),
            ],
            CompoundIndexes: []),

        // ── audit_events — Online Archive: event_at > 24 mo ──────────────
        new CollectionSpec(
            Name: "audit_events",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("action_type"),
                new("event_at"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("actor_id", 1), ("event_at", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "event_at"),

        // ── job_runs — Online Archive: started_at > 90 d ─────────────────
        new CollectionSpec(
            Name: "job_runs",
            TtlIndexes: [],
            SingleFieldIndexes:
            [
                new("started_at"),
            ],
            CompoundIndexes:
            [
                new CompoundIndexSpec([("job_type", 1), ("started_at", -1)]),
            ],
            HasOnlineArchive: true,
            OnlineArchiveField: "started_at"),
    ];
}
