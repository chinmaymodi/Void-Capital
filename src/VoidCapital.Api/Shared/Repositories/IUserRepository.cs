using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface IUserRepository
{
    Task<User?> GetByIdAsync(int id);
    Task<IEnumerable<User>> GetAllAsync();
    Task<int> UpdateCashAsync(int userId, decimal newCash);
    Task<int> UpdateCashAtomicAsync(int userId, decimal delta);
}
