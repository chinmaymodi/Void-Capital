using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public class SettingsRepository : ISettingsRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SettingsRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<UserSettings?> GetByUserIdAsync(int userId)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.UserSettings
            .Where(s => s.UserId == userId)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<UserSettings>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.UserSettings
            .OrderBy(s => s.UserId)
            .ToListAsync();
    }

    public async Task UpdateAsync(UserSettings settings)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        db.UserSettings.Update(settings);
        
        // Sync portfolio.watchlist
        var watchlist = JsonSerializer.Deserialize<List<string>>(settings.Watchlist) ?? new();
        var existing = await db.Watchlist.Where(w => w.UserId == settings.UserId).ToListAsync();
        
        // Remove symbols not in new watchlist
        var toRemove = existing.Where(w => !watchlist.Contains(w.Symbol)).ToList();
        db.Watchlist.RemoveRange(toRemove);
        
        // Add new symbols
        var existingSymbols = existing.Select(w => w.Symbol).ToHashSet();
        foreach (var symbol in watchlist)
        {
            if (!existingSymbols.Contains(symbol))
            {
                db.Watchlist.Add(new WatchlistItem 
                { 
                    UserId = settings.UserId, 
                    Symbol = symbol, 
                    AddedDate = DateOnly.FromDateTime(DateTime.UtcNow) 
                });
            }
        }
        
        await db.SaveChangesAsync();
    }
}
