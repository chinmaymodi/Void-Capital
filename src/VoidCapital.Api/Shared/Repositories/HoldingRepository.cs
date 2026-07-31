using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public class HoldingRepository : IHoldingRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public HoldingRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<IEnumerable<Holding>> GetByUserIdAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Holdings
            .Where(h => h.UserId == userId)
            .OrderBy(h => h.Symbol)
            .ToListAsync();
    }

    public async Task<Holding?> GetAsync(int userId, string symbol)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Holdings
            .Where(h => h.UserId == userId && h.Symbol == symbol)
            .FirstOrDefaultAsync();
    }

    public async Task<int> AddAsync(Holding holding)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Holdings.Add(holding);
        return await db.SaveChangesAsync();
    }

    public async Task<int> UpdateAsync(Holding holding)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Holdings.Update(holding);
        return await db.SaveChangesAsync();
    }

    public async Task<int> DeleteAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var holding = await db.Holdings.FindAsync(id);
        if (holding is null)
            return 0;

        db.Holdings.Remove(holding);
        return await db.SaveChangesAsync();
    }
}
