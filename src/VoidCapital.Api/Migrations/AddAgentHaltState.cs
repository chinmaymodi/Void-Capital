using FluentMigrator;

namespace VoidCapital.Api.Migrations;

/// <summary>
/// F12: terminal state for dead agents. A margin call squares off holdings,
/// but when the deficit exceeds holding value the cash stays below the
/// negative limit forever and interest compounds daily on the permanent
/// deficit - the paper-eval -91%/-100% drawdown shape. This column marks an
/// agent terminal: the daily cycle halts new signals, freezes interest, and
/// skips further margin calls until an admin revives the agent via the
/// settings endpoint. Default false for all existing rows.
/// </summary>
[Migration(013)]
public class AddAgentHaltState : Migration
{
    public override void Up()
    {
        Alter.Table("settings").InSchema("identity")
            .AddColumn("is_halted").AsBoolean().NotNullable().WithDefaultValue(false);
    }

    public override void Down()
    {
        Delete.Column("is_halted").FromTable("settings").InSchema("identity");
    }
}