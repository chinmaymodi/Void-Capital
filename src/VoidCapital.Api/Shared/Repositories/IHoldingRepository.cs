using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface IHoldingRepository
{
    Task<IEnumerable<Holding>> GetByUserIdAsync(int userId);
    Task<Holding?> GetAsync(int userId, string symbol);
    Task<int> AddAsync(Holding holding);
    Task<int> UpdateAsync(Holding holding);
    Task<int> DeleteAsync(int id);
}
