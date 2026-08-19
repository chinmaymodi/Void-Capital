using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Shared;
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
            var signal = perf.Signal;
            var symbol = signal?.Symbol;
            if (string.IsNullOrEmpty(symbol))
                continue;

            // D3: fresh price, not the 1h Redis cache -- the cycle just wrote
            // new signals and resolved them in the same run, so a stale quote
            // could settle a signal against yesterday's price.
            // D16: options signals (CE/PE) resolve against the contract settle
            // (fo_options), not the stock quote; an unobservable settle leaves
            // the row PENDING rather than resolving against the wrong price.
            decimal? currentPrice;
            if (signal is { InstrumentType: not "EQ" }
                && signal.Expiry is not null && signal.Strike is not null)
            {
                try
                {
                    currentPrice = await _marketData.GetOptionPriceAsync(
                        symbol, signal.Expiry.Value, signal.Strike.Value, signal.InstrumentType);
                }
                catch (NotFoundException)
                {
                    // D16: options signals (CE/PE) resolve against the contract settle
                    // (fo_options), not the stock quote; an unobservable settle leaves
                    // the row PENDING rather than resolving against the wrong price.
                    // SPF1: don't skip - fall through to the EXPIRED branch so the
                    // row resolves with ExitPrice = null instead of staying PENDING.
                    currentPrice = null;
                }
            }
            else
            {
                currentPrice = await _marketData.GetCurrentPriceFreshAsync(symbol);
            }

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
                perf.ActualReturn = perf.ExitPrice.HasValue
                    ? (perf.ExitPrice.Value - perf.EntryPrice) / perf.EntryPrice
                    : null;
                perf.ResolvedAt = DateTime.UtcNow;
                await _performanceRepo.UpdateAsync(perf);
            }
        }
    }
}
