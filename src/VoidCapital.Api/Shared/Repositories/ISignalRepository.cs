using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface ISignalRepository
{
    Task<Signal?> GetByIdAsync(int id);
    Task<IEnumerable<Signal>> GetTodaySignalsAsync(int userId);

    /// <summary>All signals recorded for a given date (any user, any status).</summary>
    Task<IEnumerable<Signal>> GetAllSignalsOnDateAsync(DateOnly date);
    Task<Signal> AddAsync(Signal signal);
    Task UpdateAsync(Signal signal);

    /// <summary>Per-model aggregates over resolved signal performance rows.</summary>
    Task<IEnumerable<ModelPerformanceDto>> GetModelPerformanceAsync();

    /// <summary>Paged list of resolved signals, optionally filtered by user/model.</summary>
    Task<(IEnumerable<ResolvedSignalDto> Items, int Total)> GetResolvedAsync(PerformanceQuery query);

    /// <summary>Count of signals per status (PENDING/APPROVED/...).</summary>
    Task<Dictionary<SignalStatus, int>> GetStatusCountsAsync();
}
