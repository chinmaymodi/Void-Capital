using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Signals;

namespace VoidCapital.Api.Shared.Repositories;

public class SignalPerformanceRepository : ISignalPerformanceRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SignalPerformanceRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IEnumerable<SignalPerformance>> GetPendingPerformancesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.SignalPerformances
            .Include(p => p.Signal)
            .Where(p => p.Outcome == null || p.Outcome == "PENDING")
            .ToListAsync();
    }

    public async Task<SignalPerformance> AddAsync(SignalPerformance performance)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.SignalPerformances.Add(performance);
        await db.SaveChangesAsync();
        return performance;
    }

    public async Task UpdateAsync(SignalPerformance performance)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        // Attach to a tracked entity and copy only the mutable fields. Using
        // db.SignalPerformances.Update(performance) would mark the whole graph
        // (including the linked signal) as Modified and rewrite created_at with
        // a Kind=Unspecified DateTime, which Npgsql rejects for timestamptz.
        var tracked = await db.SignalPerformances.FindAsync(performance.Id);
        if (tracked is null)
            return;

        tracked.Outcome = performance.Outcome;
        tracked.ExitPrice = performance.ExitPrice;
        tracked.ActualReturn = performance.ActualReturn;
        tracked.ResolvedAt = performance.ResolvedAt;
        await db.SaveChangesAsync();
    }
}
