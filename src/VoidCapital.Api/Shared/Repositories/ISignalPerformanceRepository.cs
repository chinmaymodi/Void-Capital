using VoidCapital.Api.Modules.Signals;

namespace VoidCapital.Api.Shared.Repositories;

public interface ISignalPerformanceRepository
{
    Task<IEnumerable<SignalPerformance>> GetPendingPerformancesAsync();
    Task<SignalPerformance> AddAsync(SignalPerformance performance);
    Task UpdateAsync(SignalPerformance performance);
}
