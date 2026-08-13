using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface IHoldingRepository
{
    Task<IEnumerable<Holding>> GetByUserIdAsync(int userId);
    Task<Holding?> GetAsync(int userId, string symbol);

    /// <summary>
    /// Locate a holding by its full instrument key (D16): instrument type,
    /// symbol, expiry, strike. Equity holdings have null expiry/strike.
    /// </summary>
    Task<Holding?> GetByInstrumentAsync(int userId, string instrumentType,
        string symbol, DateOnly? expiry, decimal? strike);

    Task<int> AddAsync(Holding holding);
    Task<int> UpdateAsync(Holding holding);
    Task<int> DeleteAsync(int id);
}
