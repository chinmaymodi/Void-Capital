using VoidCapital.Api.Modules.Signals;

namespace VoidCapital.Api.Shared.Repositories;

public interface ISignalRepository
{
    Task<Signal?> GetByIdAsync(int id);
    Task<IEnumerable<Signal>> GetTodaySignalsAsync(int userId);
    Task<Signal> AddAsync(Signal signal);
    Task UpdateAsync(Signal signal);
}
