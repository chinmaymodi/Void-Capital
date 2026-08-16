using FluentMigrator;

namespace VoidCapital.Api.Migrations;

[Migration(006)]
public class AddOptionsAgents : Migration
{
    public override void Up()
    {
        Insert.IntoTable("users").InSchema("identity")
            .Row(new { name = "Options-Careful", email = "agent4@voidcapital.local", starting_budget = 100000.00m, current_cash = 100000.00m })
            .Row(new { name = "Options-Reckless", email = "agent5@voidcapital.local", starting_budget = 100000.00m, current_cash = 100000.00m })
            .Row(new { name = "Intraday-Careful", email = "agent6@voidcapital.local", starting_budget = 100000.00m, current_cash = 100000.00m })
            .Row(new { name = "Intraday-Reckless", email = "agent7@voidcapital.local", starting_budget = 100000.00m, current_cash = 100000.00m });

        string watchlist = "[\"RELIANCE\",\"TCS\",\"HDFCBANK\",\"INFY\",\"ICICIBANK\",\"HINDUNILVR\",\"SBIN\",\"BHARTIARTL\",\"ITC\",\"WIPRO\"]";
        
        Insert.IntoTable("settings").InSchema("identity")
            .Row(new { user_id = 4, auto_execute = true, min_confidence = 0.50m, negative_limit = 0m, interest_rate = 0m, watchlist = watchlist })
            .Row(new { user_id = 5, auto_execute = true, min_confidence = 0.50m, negative_limit = 100000m, interest_rate = 0.1825m, watchlist = watchlist })
            .Row(new { user_id = 6, auto_execute = true, min_confidence = 0.50m, negative_limit = 0m, interest_rate = 0m, watchlist = watchlist })
            .Row(new { user_id = 7, auto_execute = true, min_confidence = 0.50m, negative_limit = 100000m, interest_rate = 0.1825m, watchlist = watchlist });
    }

    public override void Down()
    {
        Delete.FromTable("settings").InSchema("identity").Row(new { user_id = 4 });
        Delete.FromTable("settings").InSchema("identity").Row(new { user_id = 5 });
        Delete.FromTable("settings").InSchema("identity").Row(new { user_id = 6 });
        Delete.FromTable("settings").InSchema("identity").Row(new { user_id = 7 });
        Delete.FromTable("users").InSchema("identity").Row(new { id = 4 });
        Delete.FromTable("users").InSchema("identity").Row(new { id = 5 });
        Delete.FromTable("users").InSchema("identity").Row(new { id = 6 });
        Delete.FromTable("users").InSchema("identity").Row(new { id = 7 });
    }
}