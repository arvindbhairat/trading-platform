using FluentMigrator;

namespace SignalStack.SqlMigrations.Migrations;

/// <summary>
/// Initial SQL Server migration: provisions the backtest database schema.
/// Covers run headers, per-trade records, position lifecycle, and portfolio
/// performance as defined in docs/data-management.md § Backtest Database.
/// </summary>
[Migration(2)]
public sealed class InitialBacktest_002 : Migration
{
    public override void Up()
    {
        // ── Run header ────────────────────────────────────────────────
        Create.Table("BT_RunHeaders")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("run_id").AsGuid().NotNullable().Unique()
            .WithColumn("signal_type").AsString(100).NotNullable()
            .WithColumn("signal_parameters").AsString(int.MaxValue).NotNullable()
            .WithColumn("rme_profile_snapshot").AsString(int.MaxValue).NotNullable()
            .WithColumn("timeframe").AsString(20).NotNullable()
            .WithColumn("slippage").AsDecimal(10, 6).NotNullable()
            .WithColumn("commission_pct").AsDecimal(6, 4).NotNullable()
            .WithColumn("symbol_universe").AsString(int.MaxValue).NotNullable()
            .WithColumn("date_range_start").AsDate().NotNullable()
            .WithColumn("date_range_end").AsDate().NotNullable()
            .WithColumn("run_timestamp").AsDateTime2().NotNullable()
            .WithColumn("created_at").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentDateTime);

        // ── Per-trade records ─────────────────────────────────────────
        Create.Table("BT_Trades")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("run_id").AsGuid().NotNullable()
            .WithColumn("symbol").AsString(20).NotNullable()
            .WithColumn("entry_date").AsDate().NotNullable()
            .WithColumn("exit_date").AsDate().Nullable()
            .WithColumn("entry_price").AsDecimal(14, 4).NotNullable()
            .WithColumn("exit_price").AsDecimal(14, 4).Nullable()
            .WithColumn("stop_at_entry").AsDecimal(14, 4).NotNullable()
            .WithColumn("exit_reason").AsString(50).Nullable()
            .WithColumn("r_multiple").AsDecimal(10, 4).Nullable()
            .WithColumn("gross_pnl").AsDecimal(14, 2).NotNullable()
            .WithColumn("net_pnl").AsDecimal(14, 2).NotNullable()
            .WithColumn("created_at").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentDateTime);

        Create.Index("ix_bt_trades_run_id").OnTable("BT_Trades")
            .OnColumn("run_id").Ascending();

        // ── Position lifecycle ────────────────────────────────────────
        Create.Table("BT_PositionLifecycle")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("run_id").AsGuid().NotNullable()
            .WithColumn("symbol").AsString(20).NotNullable()
            .WithColumn("event_type").AsString(30).NotNullable()  // add | reduce | close
            .WithColumn("event_date").AsDate().NotNullable()
            .WithColumn("rme_state").AsString(30).Nullable()
            .WithColumn("peak_r_multiple").AsDecimal(10, 4).Nullable()
            .WithColumn("created_at").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentDateTime);

        Create.Index("ix_bt_position_lifecycle_run_id").OnTable("BT_PositionLifecycle")
            .OnColumn("run_id").Ascending();

        // ── Portfolio performance ─────────────────────────────────────
        Create.Table("BT_PortfolioPerformance")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("run_id").AsGuid().NotNullable()
            .WithColumn("equity_curve").AsString(int.MaxValue).NotNullable()  // JSON: [{date, equity}, ...]
            .WithColumn("xirr").AsDecimal(8, 4).Nullable()
            .WithColumn("sharpe_ratio").AsDecimal(8, 4).Nullable()
            .WithColumn("max_drawdown_pct").AsDecimal(8, 4).Nullable()
            .WithColumn("win_rate_pct").AsDecimal(8, 4).Nullable()
            .WithColumn("expectancy").AsDecimal(10, 4).Nullable()
            .WithColumn("profit_factor").AsDecimal(10, 4).Nullable()
            .WithColumn("starting_equity").AsDecimal(14, 2).NotNullable()
            .WithColumn("ending_equity").AsDecimal(14, 2).NotNullable()
            .WithColumn("created_at").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentDateTime);

        Create.Index("ix_bt_portfolio_performance_run_id").OnTable("BT_PortfolioPerformance")
            .OnColumn("run_id").Ascending();
    }

    public override void Down()
    {
        Delete.Table("BT_PortfolioPerformance");
        Delete.Table("BT_PositionLifecycle");
        Delete.Table("BT_Trades");
        Delete.Table("BT_RunHeaders");
    }
}
