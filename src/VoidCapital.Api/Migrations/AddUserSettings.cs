using FluentMigrator;

namespace VoidCapital.Api.Migrations;

[Migration(002)]
public class AddUserSettings : Migration
{
    public override void Up()
    {
        Create.Table("settings").InSchema("identity")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("user_id").AsInt32().ForeignKey("FK_settings_user", "identity", "users", "id")
            .WithColumn("auto_execute").AsBoolean().WithDefaultValue(false)
            .WithColumn("min_confidence").AsDecimal(3, 2).WithDefaultValue(0.50m)
            .WithColumn("negative_limit").AsDecimal(12, 2).WithDefaultValue(0m)     // margin credit line (0 = none)
            .WithColumn("interest_rate").AsDecimal(5, 4).WithDefaultValue(0m)       // annual interest rate, accrued daily (rate/365) on borrowed amount
            .WithColumn("watchlist").AsString(int.MaxValue).WithDefaultValue("[]");

        // user_id=1: normal manual trader
        Insert.IntoTable("settings").InSchema("identity")
            .Row(new { user_id = 1, auto_execute = false, min_confidence = 0.50m, negative_limit = 0m, interest_rate = 0m, watchlist = "[\"RELIANCE\",\"TCS\",\"HDFCBANK\",\"INFY\",\"ICICIBANK\",\"HINDUNILVR\",\"SBIN\",\"BHARTIARTL\",\"ITC\",\"WIPRO\"]" });

        // user_id=2: system -- disciplined, no margin
        Insert.IntoTable("settings").InSchema("identity")
            .Row(new { user_id = 2, auto_execute = true, min_confidence = 0.50m, negative_limit = 0m, interest_rate = 0m, watchlist = "[\"RELIANCE\",\"TCS\",\"HDFCBANK\",\"INFY\",\"ICICIBANK\",\"HINDUNILVR\",\"SBIN\",\"BHARTIARTL\",\"ITC\",\"WIPRO\"]" });

        // user_id=3: system-reckless -- Rs 100,000 margin line, 18.25% annual
        // interest (0.05% daily) on borrowed amount, accrued daily at rate/365
        Insert.IntoTable("settings").InSchema("identity")
            .Row(new { user_id = 3, auto_execute = true, min_confidence = 0.50m, negative_limit = 100000m, interest_rate = 0.1825m, watchlist = "[\"RELIANCE\",\"TCS\",\"HDFCBANK\",\"INFY\",\"ICICIBANK\",\"HINDUNILVR\",\"SBIN\",\"BHARTIARTL\",\"ITC\",\"WIPRO\"]" });
    }

    public override void Down()
    {
        Delete.Table("settings").InSchema("identity");
    }
}
