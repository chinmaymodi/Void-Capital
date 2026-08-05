using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Services;

/// <summary>Result of one daily-cycle run.</summary>
public record DailyCycleRunResult(string Status, int UsersProcessed, int SignalsGenerated, int SignalsExecuted, string? Error);

/// <summary>
/// Executes one full daily cycle (ticket D10.1):
///   1. Signal generation for every user (facade -> Python pipeline)
///   2. Auto-execute signals for users with AutoExecute (min-confidence gated)
///   3. Resolve pending signal performance (target/stop/expiry)
///   4. Charge daily interest on negative cash
///   5. Margin call: square off holdings below the negative limit
///   6. Record daily PnL snapshots for all users
///   7. Record the run in ops.cycle_runs (RUNNING -> SUCCEEDED/FAILED)
/// Scoped, so repositories are constructor-injected and unit-testable.
/// </summary>
public interface IDailyCycleRunner
{
    Task<DailyCycleRunResult> RunAsync(CancellationToken ct = default);
}

public class DailyCycleRunner : IDailyCycleRunner
{
    private readonly ISignalIntegrationService _signalIntegration;
    private readonly ISignalService _signalService;
    private readonly ISignalRepository _signalRepo;
    private readonly SignalPerformanceService _performanceService;
    private readonly IUserRepository _userRepo;
    private readonly IPortfolioService _portfolioService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IHoldingRepository _holdingRepo;
    private readonly ICycleRunRepository _cycleRunRepo;
    private readonly ILogger<DailyCycleRunner> _logger;

    public DailyCycleRunner(
        ISignalIntegrationService signalIntegration,
        ISignalService signalService,
        ISignalRepository signalRepo,
        SignalPerformanceService performanceService,
        IUserRepository userRepo,
        IPortfolioService portfolioService,
        ISettingsRepository settingsRepo,
        IHoldingRepository holdingRepo,
        ICycleRunRepository cycleRunRepo,
        ILogger<DailyCycleRunner> logger)
    {
        _signalIntegration = signalIntegration;
        _signalService = signalService;
        _signalRepo = signalRepo;
        _performanceService = performanceService;
        _userRepo = userRepo;
        _portfolioService = portfolioService;
        _settingsRepo = settingsRepo;
        _holdingRepo = holdingRepo;
        _cycleRunRepo = cycleRunRepo;
        _logger = logger;
    }

    public async Task<DailyCycleRunResult> RunAsync(CancellationToken ct = default)
    {
        var run = new CycleRun { StartedAt = DateTime.UtcNow, Status = "RUNNING" };
        run = await _cycleRunRepo.AddAsync(run);

        try
        {
            // 1. Signal generation for every user
            var summary = await _signalIntegration.RunForAllUsersAsync(ct);
            run.UsersProcessed = summary.UsersProcessed;

            // 2. Auto-execute signals for auto-execute users, min-confidence gated
            var executed = 0;
            var settings = (await _settingsRepo.GetAllAsync()).ToList();
            foreach (var userSettings in settings.Where(s => s.AutoExecute))
            {
                ct.ThrowIfCancellationRequested();
                var today = await _signalRepo.GetTodaySignalsAsync(userSettings.UserId);
                var eligible = today
                    .Where(s => s.Status == SignalStatus.PENDING && s.Confidence >= userSettings.MinConfidence)
                    .Select(s => s.Id)
                    .ToArray();

                if (eligible.Length == 0) continue;

                var results = await _signalService.BatchApproveAsync(eligible);
                executed += results.Count(r => r.Success);
            }
            run.SignalsExecuted = executed;

            // 3. Resolve pending signal performance
            await _performanceService.ResolvePendingSignalsAsync();

            // 4-5. Interest + margin call for every user
            var users = await _userRepo.GetAllAsync();
            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();
                var userSettings = await _settingsRepo.GetByUserIdAsync(user.Id);

                if (user.CurrentCash < 0)
                {
                    var interest = user.CurrentCash * (userSettings?.InterestRate ?? 0m) / 365;
                    await _userRepo.UpdateCashAsync(user.Id, user.CurrentCash + interest);
                }

                if (userSettings?.NegativeLimit != null && user.CurrentCash < -userSettings.NegativeLimit)
                {
                    _logger.LogWarning(
                        "Margin call for user {UserId}: cash {Cash} < limit {Limit}",
                        user.Id, user.CurrentCash, -userSettings.NegativeLimit);
                    var holdings = await _holdingRepo.GetByUserIdAsync(user.Id);
                    foreach (var holding in holdings)
                    {
                        await _portfolioService.ExecuteSellAsync(user.Id, holding.Symbol, holding.Quantity);
                    }
                }
            }

            // 6. PnL snapshots
            foreach (var user in users)
            {
                await _portfolioService.RecordDailySnapshotAsync(user.Id);
            }

            run.Status = "SUCCEEDED";
            run.SignalsGenerated = summary.UsersSucceeded;
        }
        catch (Exception ex)
        {
            run.Status = "FAILED";
            run.Error = ex.Message;
            _logger.LogError(ex, "Daily cycle failed");
        }
        finally
        {
            run.FinishedAt = DateTime.UtcNow;
            await _cycleRunRepo.UpdateAsync(run);
        }

        return new DailyCycleRunResult(run.Status, run.UsersProcessed, run.SignalsGenerated, run.SignalsExecuted, run.Error);
    }
}
