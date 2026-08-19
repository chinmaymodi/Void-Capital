using FluentMigrator;

namespace VoidCapital.Api.Migrations;

/// <summary>
/// F2: the live C# ledger previously charged zero commission per trade. A
/// TradeCostCalculator now applies the NSE cost stack (STT + turnover +
/// exchange + GST + stamp) on every equity/options fill; the commission is
/// stored per trade so the ledger is auditable. Default 0 for rows written
/// before this migration.
/// </summary>
[Migration(011)]
public class AddTradeCommission : Migration
{
    public override void Up()
    {
        Alter.Table("trade_log").InSchema("portfolio")
            .AddColumn("commission").AsDecimal(12, 2).NotNullable().WithDefaultValue(0m);
    }

    public override void Down()
    {
        Delete.Column("commission").FromTable("trade_log").InSchema("portfolio");
    }
}
