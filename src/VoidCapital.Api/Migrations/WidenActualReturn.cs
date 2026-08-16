using FluentMigrator;

namespace VoidCapital.Api.Migrations;

/// <summary>
/// F22: signal_performance.actual_return was decimal(6,4) - max 99.9999.
/// Options returns routinely exceed 100% (a 5%-premium option 5x-ing = 400%),
/// which overflowed the column (Postgres numeric overflow error on write).
/// Widen to decimal(8,4) (max 999999.9999).
/// </summary>
[Migration(010)]
public class WidenActualReturn : Migration
{
    public override void Up()
    {
        Alter.Table("signal_performance").InSchema("signals")
            .AlterColumn("actual_return").AsDecimal(8, 4).Nullable();
    }

    public override void Down()
    {
        Alter.Table("signal_performance").InSchema("signals")
            .AlterColumn("actual_return").AsDecimal(6, 4).Nullable();
    }
}