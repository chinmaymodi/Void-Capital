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

    public async Task<(IEnumerable<Trade> Items, int Total)> QueryAsync(int userId, TradeQuery query)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var filtered = db.Trades.Where(t => t.UserId == userId);

        if (!string.IsNullOrWhiteSpace(query.Symbol))
            filtered = filtered.Where(t => t.Symbol == query.Symbol.ToUpperInvariant());

        if (!string.IsNullOrWhiteSpace(query.Type))
            filtered = filtered.Where(t => t.Type == query.Type.ToUpperInvariant());

        if (query.From.HasValue)
        {
            var fromUtc = query.From.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            filtered = filtered.Where(t => t.Timestamp >= fromUtc);
        }

        if (query.To.HasValue)
        {
            var toUtc = query.To.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);
            filtered = filtered.Where(t => t.Timestamp <= toUtc);
        }

        var total = await filtered.CountAsync();

        var page = Math.Max(query.Page, 1);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var items = await filtered
            .OrderByDescending(t => t.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, total);
    }
}
