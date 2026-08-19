using FluentMigrator;

namespace VoidCapital.Api.Migrations;

/// <summary>
/// Data fix for F3: migrations 002/006 seeded every agent with
/// min_confidence = 0.50, so the careful/reckless pairs were identical except
/// the (never-binding) margin floor - the confidence dial experiment was
/// meaningless. Differentiate the dial per risk profile:
///   careful (users 2, 4, 6): 0.70 - only strong signals pass the gate
///     (EQ avg3 emits a constant 0.7, so careful trades exactly at the band
///     edge; CE avg3 confidence = min(1, |avg3|/2) needs |avg3| >= 1.4, deep
///     in the band; ensemble needs full agreement at 0.8).
///   reckless (users 3, 5, 7): 0.30 - accepts weak signals
///     (CE avg3 trades from |avg3| >= 0.6, earlier entry; ensemble majority
///     0.4 passes). Exits always carry confidence 1.0 and are never gated.
/// User 1 (manual trader, auto_execute = false) stays at 0.50.
/// Migrations 002/006 were already applied to live DBs, so this migration
/// fixes existing rows; the guard on the old seed value makes re-runs and
/// manually-tuned rows a no-op.
/// </summary>
[Migration(012)]
public class DifferentiateAgentConfidence : Migration
{
    public override void Up()
    {
        // Only touch rows still at the old (uniform) seed value so a re-run
        // or an admin-tuned row is left alone.
        Execute.Sql("""
            UPDATE identity.settings
            SET min_confidence = 0.70
            WHERE user_id IN (2, 4, 6)
              AND min_confidence = 0.50;

            UPDATE identity.settings
            SET min_confidence = 0.30
            WHERE user_id IN (3, 5, 7)
              AND min_confidence = 0.50;
            """);
    }

    public override void Down()
    {
        // Data-only migration: revert the differentiated rows to the old seed.
        Execute.Sql("""
            UPDATE identity.settings
            SET min_confidence = 0.50
            WHERE user_id IN (2, 4, 6)
              AND min_confidence = 0.70;

            UPDATE identity.settings
            SET min_confidence = 0.50
            WHERE user_id IN (3, 5, 7)
              AND min_confidence = 0.30;
            """);
    }
}