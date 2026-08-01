using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Modules.Signals.Services;

/// <summary>
/// Resolves signal performance rows once their evaluation horizon has passed.
/// A performance row is settled when the price hits the target (HIT_TARGET),
/// hits the stop loss (HIT_STOP), or the row is older than its evaluation
/// days (EXPIRED). Return is relative to the entry price.
/// </summary>
public class SignalPerformanceService
{
    private readonly ISignalPerformanceRepository _performanceRepo;
    private readonly IMarketDataService _marketData;

    public SignalPerformanceService(
        ISignalPerformanceRepository performanceRepo,
        IMarketDataService marketData)
    {
        _performanceRepo = performanceRepo;
        _marketData = marketData;
    }

    public async Task ResolvePendingSignalsAsync()
    {
        var pending = await _performanceRepo.GetPendingPerformancesAsync();

        foreach (var perf in pending)
        {
            var symbol = perf.Signal?.Symbol;
            if (string.IsNullOrEmpty(symbol))
                continue;

            var currentPrice = await _marketData.GetCurrentPriceAsync(symbol);
            var age = (DateTime.UtcNow - perf.CreatedAt).Days;

            if (perf.TargetPrice.HasValue && currentPrice >= perf.TargetPrice.Value)
            {
                perf.Outcome = "HIT_TARGET";
                perf.ExitPrice = currentPrice;
            }
            else if (perf.StopLoss.HasValue && currentPrice <= perf.StopLoss.Value)
            {
                perf.Outcome = "HIT_STOP";
                perf.ExitPrice = currentPrice;
            }
            else if (age >= perf.EvaluationDays)
            {
                perf.Outcome = "EXPIRED";
                perf.ExitPrice = currentPrice;
            }

            if (perf.Outcome is not null and not "PENDING")
            {
                perf.ActualReturn = (perf.ExitPrice!.Value - perf.EntryPrice) / perf.EntryPrice;
                perf.ResolvedAt = DateTime.UtcNow;
                await _performanceRepo.UpdateAsync(perf);
            }
        }
    }
}
