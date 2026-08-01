using FluentMigrator;

namespace VoidCapital.Api.Migrations;

[Migration(003)]
public class AddSignalPerformance : Migration
{
    public override void Up()
    {
        // model_predictions (created in 001) needs a failure_reason column so
        // the signal engine can record why an auto-executed signal failed.
        Alter.Table("model_predictions").InSchema("signals")
            .AddColumn("failure_reason").AsString().Nullable();

        Create.Table("signal_performance").InSchema("signals")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("signal_id").AsInt32().NotNullable()
                .ForeignKey("FK_signal_performance_signal", "signals", "model_predictions", "id")
            .WithColumn("entry_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("target_price").AsDecimal(10, 2).Nullable()
            .WithColumn("stop_loss").AsDecimal(10, 2).Nullable()
            .WithColumn("exit_price").AsDecimal(10, 2).Nullable()
            .WithColumn("outcome").AsString().Nullable()
            .WithColumn("actual_return").AsDecimal(6, 4).Nullable()
            .WithColumn("evaluation_days").AsInt32().WithDefaultValue(5)
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentDateTime)
            .WithColumn("resolved_at").AsDateTime().Nullable();
    }

    public override void Down()
    {
        Delete.Table("signal_performance").InSchema("signals");
        Delete.Column("failure_reason").FromTable("model_predictions").InSchema("signals");
    }
}
