using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Modules.Signals.Services;

/// <summary>
/// Approval workflow for model signals. With auto-execute disabled a pending
/// signal is simply approved; with auto-execute enabled the trade runs
/// immediately through the portfolio engine and the signal is marked EXECUTED,
/// or FAILED with the engine's reason when execution cannot complete.
/// </summary>
public class SignalService : ISignalService
{
    private readonly ISignalRepository _signalRepo;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IPortfolioService _portfolioService;

    public SignalService(
        ISignalRepository signalRepo,
        ISettingsRepository settingsRepo,
        IPortfolioService portfolioService)
    {
        _signalRepo = signalRepo;
        _settingsRepo = settingsRepo;
        _portfolioService = portfolioService;
    }

    public async Task<IEnumerable<SignalDto>> GetTodaySignalsAsync(int userId)
    {
        var signals = await _signalRepo.GetTodaySignalsAsync(userId);
        return signals.Select(SignalDto.From);
    }

    public async Task<SignalDto> ApproveSignalAsync(int signalId)
    {
        var signal = await GetPendingOrThrowAsync(signalId);

        var settings = await _settingsRepo.GetByUserIdAsync(signal.UserId);
        if (settings?.AutoExecute == true)
        {
            await TryExecuteAsync(signal);
        }
        else
        {
            signal.Status = SignalStatus.APPROVED;
        }

        await _signalRepo.UpdateAsync(signal);
        return SignalDto.From(signal);
    }

    public async Task<SignalDto> RejectSignalAsync(int signalId)
    {
        var signal = await GetPendingOrThrowAsync(signalId);
        signal.Status = SignalStatus.REJECTED;

        await _signalRepo.UpdateAsync(signal);
        return SignalDto.From(signal);
    }

    public async Task<IEnumerable<SignalBatchResult>> BatchApproveAsync(int[] ids)
    {
        var results = new List<SignalBatchResult>();
        foreach (var id in ids)
        {
            try
            {
                await ApproveSignalAsync(id);
                results.Add(SignalBatchResult.Ok(id));
            }
            catch (Exception ex)
            {
                results.Add(SignalBatchResult.Failed(id, ex.Message));
            }
        }
        return results;
    }

    public async Task<IEnumerable<SignalBatchResult>> BatchRejectAsync(int[] ids)
    {
        var results = new List<SignalBatchResult>();
        foreach (var id in ids)
        {
            try
            {
                await RejectSignalAsync(id);
                results.Add(SignalBatchResult.Ok(id));
            }
            catch (Exception ex)
            {
                results.Add(SignalBatchResult.Failed(id, ex.Message));
            }
        }
        return results;
    }

    private async Task<Signal> GetPendingOrThrowAsync(int signalId)
    {
        var signal = await _signalRepo.GetByIdAsync(signalId)
            ?? throw new NotFoundException($"Signal {signalId} was not found.");

        if (signal.Status != SignalStatus.PENDING)
            throw new ValidationException($"Signal {signalId} has already been processed.");

        return signal;
    }

    private async Task TryExecuteAsync(Signal signal)
    {
        try
        {
            if (signal.Action == "BUY" || signal.Action == "SELL")
            {
                if (signal.SuggestedQuantity is null or <= 0)
                    throw new ValidationException("Signal has no suggested quantity.");

                if (signal.Action == "BUY")
                {
                    await _portfolioService.ExecuteBuyAsync(signal.UserId, signal.Symbol, signal.SuggestedQuantity.Value);
                }
                else
                {
                    await _portfolioService.ExecuteSellAsync(signal.UserId, signal.Symbol, signal.SuggestedQuantity.Value);
                }
            }

            signal.Status = SignalStatus.EXECUTED;
        }
        catch (Exception ex)
        {
            signal.Status = SignalStatus.FAILED;
            signal.FailureReason = ex.Message;
        }
    }
}
