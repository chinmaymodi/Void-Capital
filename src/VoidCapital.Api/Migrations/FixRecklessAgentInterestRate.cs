using FluentMigrator;

namespace VoidCapital.Api.Migrations;

/// <summary>
/// Data fix for F21: migrations 002/006 seeded reckless agents (users 3, 5, 7)
/// with interest_rate = 0.0005 while the comment claimed "0.05% daily
/// interest". DailyCycleRunner accrues at rate/365 (ANNUAL semantics), so the
/// seeded value actually charged ~0.05%/yr - 365x weaker than intended.
/// Decision: keep annual semantics (rate/365 daily accrual is the standard
/// convention); the correct seed for "0.05% daily" is 0.0005 * 365 = 0.1825
/// (18.25% annual). Migrations 002/006 were already applied to live DBs, so
/// this migration fixes existing rows; the seed values in 002/006 were also
/// corrected for fresh installs.
/// </summary>
[Migration(009)]
public class FixRecklessAgentInterestRate : Migration
{
    public override void Up()
    {
        // Only touch rows still at the old (wrong) seed value so a re-run or
        // a fresh install that already has 0.1825 is a no-op.
        Execute.Sql("""
            UPDATE identity.settings
            SET interest_rate = 0.1825
            WHERE user_id IN (3, 5, 7)
              AND interest_rate = 0.0005;
            """);
    }

    public override void Down()
    {
        // Data-only migration: revert the three rows to the old seed value.
        Execute.Sql("""
            UPDATE identity.settings
            SET interest_rate = 0.0005
            WHERE user_id IN (3, 5, 7)
              AND interest_rate = 0.1825;
            """);
    }
}