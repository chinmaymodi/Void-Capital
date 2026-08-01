using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface ITradeRepository
{
    Task<int> AddAsync(Trade trade);
    Task<IEnumerable<Trade>> GetByUserIdAsync(int userId);

    /// <summary>Paged trade log with optional symbol/type/date filters.</summary>
    Task<(IEnumerable<Trade> Items, int Total)> QueryAsync(int userId, TradeQuery query);
}
