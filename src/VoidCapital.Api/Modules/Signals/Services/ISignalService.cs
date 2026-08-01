using VoidCapital.Api.Modules.Signals.DTOs;

namespace VoidCapital.Api.Modules.Signals.Services;

public interface ISignalService
{
    Task<IEnumerable<SignalDto>> GetTodaySignalsAsync(int userId);
    Task<SignalDto> ApproveSignalAsync(int signalId);
    Task<SignalDto> RejectSignalAsync(int signalId);
    Task<IEnumerable<SignalBatchResult>> BatchApproveAsync(int[] ids);
    Task<IEnumerable<SignalBatchResult>> BatchRejectAsync(int[] ids);
}
