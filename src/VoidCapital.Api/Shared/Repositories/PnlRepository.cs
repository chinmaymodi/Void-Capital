using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public class PnlRepository : IPnlRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public PnlRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<int> AddAsync(PnlSnapshot snapshot)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.PnlSnapshots.Add(snapshot);
        return await db.SaveChangesAsync();
    }

    public async Task<IEnumerable<PnlSnapshot>> GetByUserIdAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.PnlSnapshots
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Date)
            .ToListAsync();
    }

    public async Task<PnlSnapshot?> GetLatestAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.PnlSnapshots
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.Date)
            .FirstOrDefaultAsync();
    }
}
