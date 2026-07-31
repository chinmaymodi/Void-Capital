using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface IPnlRepository
{
    Task<int> AddAsync(PnlSnapshot snapshot);
    Task<IEnumerable<PnlSnapshot>> GetByUserIdAsync(int userId);
    Task<PnlSnapshot?> GetLatestAsync(int userId);
}
