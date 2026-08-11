using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public class CycleRunRepository : ICycleRunRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public CycleRunRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<CycleRun> AddAsync(CycleRun run)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.CycleRuns.Add(run);
        await db.SaveChangesAsync();
        return run;
    }

    public async Task<CycleRun> UpdateAsync(CycleRun run)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Attach to a tracked entity and copy only the mutable fields. Using
        // db.CycleRuns.Update(run) would mark every property as Modified and
        // rewrite started_at with a Kind=Unspecified DateTime (read back from
        // a timestamp-without-time-zone column), which Npgsql rejects for
        // timestamptz. Same pattern as SignalRepository/SignalPerformanceRepository.
        var tracked = await db.CycleRuns.FindAsync(run.Id);
        if (tracked is null)
            return run;

        tracked.Status = run.Status;
        tracked.Error = run.Error;
        tracked.FinishedAt = run.FinishedAt;
        tracked.SignalsGenerated = run.SignalsGenerated;
        tracked.SignalsExecuted = run.SignalsExecuted;
        tracked.UsersProcessed = run.UsersProcessed;
        await db.SaveChangesAsync();
        return tracked;
    }

    public async Task<IEnumerable<CycleRun>> GetRecentAsync(int limit)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.CycleRuns
            .OrderByDescending(r => r.StartedAt)
            .Take(limit)
            .ToListAsync();
    }
}
