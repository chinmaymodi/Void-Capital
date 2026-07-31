using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface ITradeRepository
{
    Task<int> AddAsync(Trade trade);
    Task<IEnumerable<Trade>> GetByUserIdAsync(int userId);
}
