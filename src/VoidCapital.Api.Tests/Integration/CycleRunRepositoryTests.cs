using FluentAssertions;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// CycleRunRepository against real PostgreSQL. Regression for the stale-run
/// abort path: ops.cycle_runs timestamps are timestamp-without-time-zone, so a
/// read-then-update must copy only mutable fields -- a naive db.Update(run)
/// rewrites started_at with a Kind=Unspecified DateTime, which Npgsql rejects
/// for timestamptz and the catch-up check crashes on every startup.
/// </summary>
[Collection("integration")]
public class CycleRunRepositoryTests
{
    private readonly IntegrationFactory _factory;

    public CycleRunRepositoryTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateAsync_ReadThenUpdate_RoundTripsWithoutKindError()
    {
        var repo = new CycleRunRepository(_factory.DbFactory);

        var added = await repo.AddAsync(new CycleRun
        {
            StartedAt = DateTime.UtcNow,
            Status = "RUNNING"
        });

        // Read back exactly like DailyCycleService.RunCatchUpIfMissedAsync does.
        // Search recent runs rather than assuming ours is the newest: the host's
        // own boot catch-up cycle can insert a run concurrently with this test.
        var read = (await repo.GetRecentAsync(10)).Single(r => r.Id == added.Id);
        read.Status.Should().Be("RUNNING");

        // The stale-run abort path: mark FAILED with a fresh FinishedAt.
        read.Status = "FAILED";
        read.Error = "Aborted on startup: run stuck in RUNNING";
        read.FinishedAt = DateTime.UtcNow;

        var updated = await repo.UpdateAsync(read);

        updated.Status.Should().Be("FAILED");
        updated.Error.Should().Contain("stuck in RUNNING");
        updated.FinishedAt.Should().NotBeNull();

        // Persisted, and the untouched started_at survived the update.
        var reloaded = (await repo.GetRecentAsync(10)).Single(r => r.Id == added.Id);
        reloaded.Status.Should().Be("FAILED");
        reloaded.StartedAt.Should().BeCloseTo(added.StartedAt, TimeSpan.FromSeconds(1));
    }
}