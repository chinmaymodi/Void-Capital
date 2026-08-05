using FluentMigrator;

namespace VoidCapital.Api.Migrations;

/// <summary>
/// Backfill portfolio.watchlist from identity.settings.watchlist (JSON array
/// string). The settings JSON is the single source of truth; the normalized
/// table is the projection the Python pipeline reads (generate_signals.py
/// load_watchlist). Migration 001 created the table but nothing ever populated
/// it -- the smoke test found 0 rows and the pipeline logged "watchlist is
/// empty". ON CONFLICT DO NOTHING makes the backfill idempotent: re-runs and
/// fresh installs both converge.
/// </summary>
[Migration(004)]
public class AddWatchlistBackfill : Migration
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
