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
        await db.SaveChangesAsync();
    }
}
