using FluentMigrator;

namespace VoidCapital.Api.Migrations;

[Migration(001)]
public class InitialSchema : Migration
{
    public override void Up()
    {
        Create.Schema("identity");
        Create.Schema("market_data");
        Create.Schema("portfolio");
        Create.Schema("signals");
        Create.Schema("ml");

        Create.Table("users").InSchema("identity")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("name").AsString().NotNullable()
            .WithColumn("email").AsString().NotNullable().Unique()
            .WithColumn("starting_budget").AsDecimal(12, 2).NotNullable().WithDefaultValue(100000.00m)
            .WithColumn("current_cash").AsDecimal(12, 2).NotNullable().WithDefaultValue(100000.00m)
            .WithColumn("created_at").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentDateTime);

        Create.Table("stocks").InSchema("market_data")
            .WithColumn("symbol").AsString().NotNullable().PrimaryKey()
            .WithColumn("date").AsDate().NotNullable().PrimaryKey()
            .WithColumn("open").AsDecimal(10, 2).NotNullable()
            .WithColumn("high").AsDecimal(10, 2).NotNullable()
            .WithColumn("low").AsDecimal(10, 2).NotNullable()
            .WithColumn("close").AsDecimal(10, 2).NotNullable()
            .WithColumn("volume").AsInt64().NotNullable();

        Create.Table("holdings").InSchema("portfolio")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("user_id").AsInt32().NotNullable().ForeignKey("FK_holdings_user", "identity", "users", "id")
            .WithColumn("instrument_type").AsString().NotNullable().WithDefaultValue("EQ")
            .WithColumn("symbol").AsString().NotNullable()
            .WithColumn("expiry").AsDate().Nullable()
            .WithColumn("strike").AsDecimal(10, 2).Nullable()
            .WithColumn("quantity").AsInt32().NotNullable()
            .WithColumn("avg_price").AsDecimal(10, 2).NotNullable()
            .WithColumn("buy_date").AsDate().NotNullable().WithDefault(SystemMethods.CurrentDateTime);
        // PostgreSQL treats NULLs as distinct in plain unique constraints, so a
        // constraint on (user_id, instrument_type, symbol, expiry, strike) would
        // never dedupe equity holdings (NULL expiry/strike). Use an expression
        // index that coalesces NULLs -- matches the intended design in CONTEXT.md.
        Execute.Sql("""
            CREATE UNIQUE INDEX uq_holdings_user_instrument
            ON portfolio.holdings (user_id, instrument_type, symbol,
                                   COALESCE(expiry, '1900-01-01'),
                                   COALESCE(strike, 0));
            """);

        Create.Table("trade_log").InSchema("portfolio")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("user_id").AsInt32().NotNullable().ForeignKey("FK_trade_log_user", "identity", "users", "id")
            .WithColumn("instrument_type").AsString().NotNullable().WithDefaultValue("EQ")
            .WithColumn("symbol").AsString().NotNullable()
            .WithColumn("expiry").AsDate().Nullable()
            .WithColumn("strike").AsDecimal(10, 2).Nullable()
            .WithColumn("type").AsString().NotNullable()
            .WithColumn("quantity").AsInt32().NotNullable()
            .WithColumn("price").AsDecimal(10, 2).NotNullable()
            .WithColumn("total_value").AsDecimal(12, 2).NotNullable()
            .WithColumn("reason").AsString().Nullable()
            .WithColumn("timestamp").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentDateTime);

        Create.Table("pnl_snapshots").InSchema("portfolio")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("user_id").AsInt32().NotNullable().ForeignKey("FK_pnl_snapshots_user", "identity", "users", "id")
            .WithColumn("date").AsDate().NotNullable().WithDefault(SystemMethods.CurrentDateTime)
            .WithColumn("portfolio_value").AsDecimal(12, 2).NotNullable()
            .WithColumn("cash_value").AsDecimal(12, 2).NotNullable()
            .WithColumn("holdings_value").AsDecimal(12, 2).NotNullable();

        Create.Table("watchlist").InSchema("portfolio")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("user_id").AsInt32().NotNullable().ForeignKey("FK_watchlist_user", "identity", "users", "id")
            .WithColumn("symbol").AsString().NotNullable()
            .WithColumn("added_date").AsDate().NotNullable().WithDefault(SystemMethods.CurrentDateTime);
        Create.UniqueConstraint("uq_watchlist_user_symbol").OnTable("watchlist").WithSchema("portfolio")
            .Columns("user_id", "symbol");

        Create.Table("model_predictions").InSchema("signals")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("user_id").AsInt32().NotNullable().ForeignKey("FK_model_predictions_user", "identity", "users", "id")
            .WithColumn("date").AsDate().NotNullable().WithDefault(SystemMethods.CurrentDateTime)
            .WithColumn("instrument_type").AsString().NotNullable().WithDefaultValue("EQ")
            .WithColumn("symbol").AsString().NotNullable()
            .WithColumn("expiry").AsDate().Nullable()
            .WithColumn("strike").AsDecimal(10, 2).Nullable()
            .WithColumn("model_name").AsString().NotNullable()
            .WithColumn("action").AsString().NotNullable()
            .WithColumn("confidence").AsDecimal(4, 3).NotNullable()
            .WithColumn("reason").AsString().Nullable()
            .WithColumn("suggested_quantity").AsInt32().Nullable()
            .WithColumn("status").AsString().NotNullable().WithDefaultValue("PENDING");

        Create.Table("backtest_results").InSchema("signals")
            .WithColumn("id").AsInt32().PrimaryKey().Identity()
            .WithColumn("model_name").AsString().NotNullable()
            .WithColumn("date_run").AsDateTime().NotNullable().WithDefault(SystemMethods.CurrentDateTime)
            .WithColumn("total_return").AsDecimal(6, 4).Nullable()
            .WithColumn("sharpe_ratio").AsDecimal(6, 4).Nullable()
            .WithColumn("max_drawdown").AsDecimal(6, 4).Nullable()
            .WithColumn("win_rate").AsDecimal(5, 4).Nullable()
            .WithColumn("num_trades").AsInt32().Nullable()
            .WithColumn("benchmark_return").AsDecimal(6, 4).Nullable();

        // Seed 3 demo users: Trader One (manual), System, System-Reckless
        Insert.IntoTable("users").InSchema("identity")
            .Row(new { name = "Trader One", email = "trader@voidcapital.local", starting_budget = 100000.00m, current_cash = 100000.00m });
        Insert.IntoTable("users").InSchema("identity")
            .Row(new { name = "System Portfolio", email = "system@voidcapital.local", starting_budget = 100000.00m, current_cash = 100000.00m });
        Insert.IntoTable("users").InSchema("identity")
            .Row(new { name = "System-Reckless", email = "reckless@voidcapital.local", starting_budget = 100000.00m, current_cash = 100000.00m });
    }

    public override void Down()
    {
        Execute.Sql("DROP INDEX IF EXISTS portfolio.uq_holdings_user_instrument;");
        Delete.Table("backtest_results").InSchema("signals");
        Delete.Table("watchlist").InSchema("portfolio");
        Delete.Table("model_predictions").InSchema("signals");
        Delete.Table("pnl_snapshots").InSchema("portfolio");
        Delete.Table("trade_log").InSchema("portfolio");
        Delete.Table("holdings").InSchema("portfolio");
        Delete.Table("stocks").InSchema("market_data");
        Delete.Table("users").InSchema("identity");
        Delete.Schema("ml");
        Delete.Schema("signals");
        Delete.Schema("portfolio");
        Delete.Schema("market_data");
        Delete.Schema("identity");
    }
}
