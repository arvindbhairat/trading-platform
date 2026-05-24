using FluentMigrator;

namespace SignalStack.SqlMigrations.Migrations;

/// <summary>
/// Initial SQL Server migration: provisions the market-data database schema.
/// Per-symbol D_ W_ M_ tables are created dynamically by HistoricDataSeed
/// (REQ-HIST-009a) using a shared DDL template so they cannot drift from
/// the schema defined here.  Fleet-wide schema evolution across existing
/// per-symbol tables is governed by REQ-MIGRATION-004 (batched background
/// migration triggered from the admin portal).
/// </summary>
[Migration(1)]
public sealed class InitialMarketData_001 : Migration
{
    public override void Up()
    {
        // Symbol master — one row per Nifty 500 symbol.
        // The sql_table_name_suffix is immutable once assigned
        // (REQ-HIST-008) and is used to construct per-symbol
        // D_, W_, M_ table names.
        Create.Table("symbol_master")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("symbol").AsString(20).NotNullable().Unique()
            .WithColumn("company_name").AsString(200).NotNullable()
            .WithColumn("sql_table_name_suffix").AsString(100).NotNullable().Unique()
            .WithColumn("is_active").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("is_archived").AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn("created_at").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentDateTime)
            .WithColumn("updated_at").AsDateTime2().NotNullable().WithDefault(SystemMethods.CurrentDateTime);

        // Index for active-symbol lookups
        Create.Index("ix_symbol_master_active")
            .OnTable("symbol_master")
            .OnColumn("is_active")
            .Ascending();
    }

    public override void Down()
    {
        Delete.Table("symbol_master");
    }
}
