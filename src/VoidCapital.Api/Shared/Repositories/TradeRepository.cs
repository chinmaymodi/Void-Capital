using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public class TradeRepository : ITradeRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public TradeRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<int> AddAsync(Trade trade)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.Trades.Add(trade);
        return await db.SaveChangesAsync();
    }

    public async Task<IEnumerable<Trade>> GetByUserIdAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Trades
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Timestamp)
            .ToListAsync();
    }
}
