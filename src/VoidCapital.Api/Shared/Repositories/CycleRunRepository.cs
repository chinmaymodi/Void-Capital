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
        db.CycleRuns.Update(run);
        await db.SaveChangesAsync();
        return run;
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
