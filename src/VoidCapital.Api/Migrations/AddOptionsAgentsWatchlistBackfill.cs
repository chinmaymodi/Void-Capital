using FluentMigrator;

namespace VoidCapital.Api.Migrations;

/// <summary>
/// Backfill portfolio.watchlist from identity.settings.watchlist (JSON array
/// string). Migration 004 ran before users 4-7 (options agents, migration 006)
/// existed, so their settings rows were never projected into the watchlist
/// table the Python pipeline reads. This migration re-runs the same generic
/// projection: every user with a settings row converges to a full watchlist.
/// ON CONFLICT DO NOTHING keeps it idempotent for re-runs and fresh installs.
/// </summary>
[Migration(008)]
public class AddOptionsAgentsWatchlistBackfill : Migration
{
    public override void Up()
    {
        Execute.Sql("""
            INSERT INTO portfolio.watchlist (user_id, symbol)
            SELECT s.user_id, w.symbol
            FROM identity.settings s,
                 json_array_elements_text(s.watchlist::json) AS w(symbol)
            ON CONFLICT (user_id, symbol) DO NOTHING;
            """);
    }

    public override void Down()
    {
        // Data-only migration: nothing structural to drop. A re-run of Up is
        // already safe (ON CONFLICT DO NOTHING), so Down is intentionally empty.
    }
}
