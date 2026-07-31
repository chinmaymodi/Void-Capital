using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;

namespace VoidCapital.Api.Modules.MarketData;

public class MarketDataRepository : IMarketDataRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public MarketDataRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<decimal?> GetLatestPriceAsync(string symbol)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.StockPrices
            .Where(s => s.Symbol == symbol)
            .OrderByDescending(s => s.Date)
            .Select(s => (decimal?)s.Close)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.StockPrices
            .Where(s => s.Symbol == symbol && s.Date >= from && s.Date <= to)
            .OrderBy(s => s.Date)
            .ToListAsync();
    }
}
