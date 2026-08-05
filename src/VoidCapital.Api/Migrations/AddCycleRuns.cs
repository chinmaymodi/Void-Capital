using FluentMigrator;

namespace VoidCapital.Api.Migrations;

/// <summary>
/// Operational table recording every daily-cycle run: when it started,
/// whether it succeeded, what it produced. Gives the admin panel a run
/// history instead of only log lines (ticket D10.1).
/// </summary>
[Migration(005)]
public class AddCycleRuns : Migration
{
    public override void Up()
    {
        Create.Schema("ops");

        Create.Table("cycle_runs").InSchema("ops")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("started_at").AsDateTime().NotNullable()
                .WithDefault(SystemMethods.CurrentDateTime)
            .WithColumn("finished_at").AsDateTime().Nullable()
            .WithColumn("status").AsString().NotNullable().WithDefaultValue("RUNNING")
            .WithColumn("error").AsString().Nullable()
            .WithColumn("signals_generated").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("signals_executed").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("users_processed").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Table("cycle_runs").InSchema("ops");
        Delete.Schema("ops");
    }
}
