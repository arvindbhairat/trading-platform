using FluentMigrator;

namespace SignalStack.SqlMigrations.Migrations;

/// <summary>
/// Adds metrics_json column to BT_RunHeaders for storing full backtest result
/// metadata (sortino, avg R, recovery factor, etc.) as JSON.
/// Needed by P4-T4 GetRunAsync deserialization.
/// </summary>
[Migration(3)]
public sealed class AddBacktestMetrics_003 : Migration
{
    public override void Up()
    {
        // Store full result metrics JSON for complete GetRunAsync reconstruction.
        // Avoids adding 10+ individual columns to BT_PortfolioPerformance.
        Alter.Table("BT_RunHeaders")
            .AddColumn("metrics_json").AsString(int.MaxValue).Nullable()
            .AddColumn("starting_equity").AsDecimal(14, 2).Nullable()
            .AddColumn("ending_equity").AsDecimal(14, 2).Nullable()
            .AddColumn("total_return_pct").AsDecimal(10, 4).Nullable()
            .AddColumn("total_return_absolute").AsDecimal(14, 2).Nullable()
            .AddColumn("sortino_ratio").AsDecimal(8, 4).Nullable()
            .AddColumn("recovery_factor").AsDecimal(10, 4).Nullable()
            .AddColumn("average_r").AsDecimal(10, 4).Nullable()
            .AddColumn("adjusted_expectancy").AsDecimal(10, 4).Nullable()
            .AddColumn("max_drawdown_in_r").AsDecimal(10, 4).Nullable()
            .AddColumn("low_confidence").AsBoolean().Nullable()
            .AddColumn("universe_coverage_pct").AsDecimal(8, 4).Nullable()
            .AddColumn("total_trades").AsInt32().Nullable()
            .AddColumn("winning_trades").AsInt32().Nullable()
            .AddColumn("losing_trades").AsInt32().Nullable()
            .AddColumn("skipped_sessions").AsInt32().Nullable()
            .AddColumn("total_sessions").AsInt32().Nullable()
            .AddColumn("survivorship_bias_discount_pct").AsDecimal(8, 4).Nullable()
            .AddColumn("raw_expectancy").AsDecimal(10, 4).Nullable()
            .AddColumn("rme_config_json").AsString(int.MaxValue).Nullable();
    }

    public override void Down()
    {
        Delete.Column("metrics_json").FromTable("BT_RunHeaders");
        Delete.Column("starting_equity").FromTable("BT_RunHeaders");
        Delete.Column("ending_equity").FromTable("BT_RunHeaders");
        Delete.Column("total_return_pct").FromTable("BT_RunHeaders");
        Delete.Column("total_return_absolute").FromTable("BT_RunHeaders");
        Delete.Column("sortino_ratio").FromTable("BT_RunHeaders");
        Delete.Column("recovery_factor").FromTable("BT_RunHeaders");
        Delete.Column("average_r").FromTable("BT_RunHeaders");
        Delete.Column("adjusted_expectancy").FromTable("BT_RunHeaders");
        Delete.Column("max_drawdown_in_r").FromTable("BT_RunHeaders");
        Delete.Column("low_confidence").FromTable("BT_RunHeaders");
        Delete.Column("universe_coverage_pct").FromTable("BT_RunHeaders");
        Delete.Column("total_trades").FromTable("BT_RunHeaders");
        Delete.Column("winning_trades").FromTable("BT_RunHeaders");
        Delete.Column("losing_trades").FromTable("BT_RunHeaders");
        Delete.Column("skipped_sessions").FromTable("BT_RunHeaders");
        Delete.Column("total_sessions").FromTable("BT_RunHeaders");
        Delete.Column("survivorship_bias_discount_pct").FromTable("BT_RunHeaders");
        Delete.Column("raw_expectancy").FromTable("BT_RunHeaders");
        Delete.Column("rme_config_json").FromTable("BT_RunHeaders");
    }
}
