using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Data;
using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public class UserRepository : IUserRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public UserRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<User?> GetByIdAsync(int id)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users.FindAsync(id);
    }

    public async Task<IEnumerable<User>> GetAllAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users
            .OrderBy(u => u.Id)
            .ToListAsync();
    }

    public async Task<int> UpdateCashAsync(int userId, decimal newCash)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CurrentCash, newCash));
    }

    public async Task<int> UpdateCashAtomicAsync(int userId, decimal delta)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.CurrentCash, u => u.CurrentCash + delta));
    }
}
