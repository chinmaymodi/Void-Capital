using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface ICycleRunRepository
{
    Task<CycleRun> AddAsync(CycleRun run);
    Task<CycleRun> UpdateAsync(CycleRun run);
    Task<IEnumerable<CycleRun>> GetRecentAsync(int limit);
}
