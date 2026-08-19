using Microsoft.Extensions.Options;
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
///   0. Refresh daily features (D1: refresh_daily.py -> market_data.features)
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
    private readonly IPythonBridge _pythonBridge;
    private readonly ISignalIntegrationService _signalIntegration;
    private readonly ISignalService _signalService;
    private readonly ISignalRepository _signalRepo;
    private readonly SignalPerformanceService _performanceService;
    private readonly IUserRepository _userRepo;
    private readonly IPortfolioService _portfolioService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IHoldingRepository _holdingRepo;
    private readonly ICycleRunRepository _cycleRunRepo;
    private readonly IProcessRunner _processRunner;
    private readonly ICycleLock _cycleLock;
    private readonly PythonSettings _pythonSettings;
    private readonly ILogger<DailyCycleRunner> _logger;

    public DailyCycleRunner(
        IPythonBridge pythonBridge,
        ISignalIntegrationService signalIntegration,
        ISignalService signalService,
        ISignalRepository signalRepo,
        SignalPerformanceService performanceService,
        IUserRepository userRepo,
        IPortfolioService portfolioService,
        ISettingsRepository settingsRepo,
        IHoldingRepository holdingRepo,
        ICycleRunRepository cycleRunRepo,
        IProcessRunner processRunner,
        ICycleLock cycleLock,
        IOptions<PythonSettings> pythonOptions,
        ILogger<DailyCycleRunner> logger)
    {
        _pythonBridge = pythonBridge;
        _signalIntegration = signalIntegration;
        _signalService = signalService;
        _signalRepo = signalRepo;
        _performanceService = performanceService;
        _userRepo = userRepo;
        _portfolioService = portfolioService;
        _settingsRepo = settingsRepo;
        _holdingRepo = holdingRepo;
        _cycleRunRepo = cycleRunRepo;
        _processRunner = processRunner;
        _cycleLock = cycleLock;
        _pythonSettings = pythonOptions.Value;
        _logger = logger;
    }

    public async Task<DailyCycleRunResult> RunAsync(CancellationToken ct = default)
    {
        // DS1: exactly one instance may run the cycle at a time. A held lock
        // means another host (or a manual trigger) is already running it -
        // skip without recording a run so catch-up still fires if needed.
        await using var lease = await _cycleLock.TryAcquireAsync(ct);
        if (lease is null)
        {
            _logger.LogWarning("Daily cycle skipped: another instance holds the advisory lock");
            return new DailyCycleRunResult(
                "SKIPPED", 0, 0, 0, "Another instance is running the daily cycle");
        }

        var startedAt = DateTime.UtcNow;
        var run = new CycleRun { StartedAt = startedAt, Status = "RUNNING" };
        run = await _cycleRunRepo.AddAsync(run);

        try
        {
            // 0. Refresh daily features (D1). Approved behavior: a failed
            // refresh logs and continues on yesterday's features -- the cycle
            // must not die because the IV computation was slow or the data
            // feed hiccuped.
            try
            {
                var refresh = await _pythonBridge.RunDataRefreshAsync(ct);
                if (refresh.Success)
                {
                    _logger.LogInformation("Feature refresh completed");
                }
                else
                {
                    _logger.LogWarning(
                        "Feature refresh failed, continuing on yesterday's features: {Error}",
                        refresh.Error);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Feature refresh threw, continuing on yesterday's features");
            }

            // 1. Signal generation for every user
            var summary = await _signalIntegration.RunForAllUsersAsync(ct);
            run.UsersProcessed = summary.UsersProcessed;

            // Real signal count written by the Python pipeline for today's IST
            // trading date (cycle runs post-close at 12:30 UTC = 18:30 IST, so
            // the UTC and IST calendar dates coincide).
            var istToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
            run.SignalsGenerated = (await _signalRepo.GetAllSignalsOnDateAsync(istToday))?.Count() ?? 0;

            // 2. Auto-execute signals for auto-execute users, min-confidence gated.
            // F12: halted agents are terminal - they never auto-execute again.
            var executed = 0;
            var settings = (await _settingsRepo.GetAllAsync()).ToList();
            foreach (var userSettings in settings.Where(s => s.AutoExecute && !s.IsHalted))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var today = await _signalRepo.GetTodaySignalsAsync(userSettings.UserId);
                    var eligible = today
                        .Where(s => s.Status == SignalStatus.PENDING && s.Confidence >= userSettings.MinConfidence)
                        .Select(s => s.Id)
                        .ToArray();

                    if (eligible.Length == 0) continue;

                    var results = await _signalService.BatchApproveAsync(eligible);
                    executed += results.Count(r => r.Success);
                }
                catch (Exception ex)
                {
                    // D1: one user's execution failure must not abort the cycle
                    // for the remaining users.
                    _logger.LogError(ex,
                        "Auto-execute failed for user {UserId}, continuing",
                        userSettings.UserId);
                }
            }
            run.SignalsExecuted = executed;

            // 3. Resolve pending signal performance
            await _performanceService.ResolvePendingSignalsAsync();

            // 4-5. Interest + margin call for every user
            var users = await _userRepo.GetAllAsync();
            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var userSettings = await _settingsRepo.GetByUserIdAsync(user.Id);

                    // F12: a halted agent is terminal - no interest accrual,
                    // no further margin calls. Only an admin revive (settings
                    // PUT with explicit IsHalted=false) brings it back.
                    if (userSettings?.IsHalted == true)
                    {
                        _logger.LogInformation(
                            "User {UserId} is halted: skipping interest and margin call",
                            user.Id);
                        continue;
                    }

                    if (user.CurrentCash < 0)
                    {
                        var interest = user.CurrentCash * (userSettings?.InterestRate ?? 0m) / 365;
                        await _userRepo.UpdateCashAsync(user.Id, user.CurrentCash + interest);
                    }

                    // D2: re-read the balance after the interest write so a
                    // breach caused by the interest charge is caught today,
                    // not detected one day late.
                    var cash = (await _userRepo.GetByIdAsync(user.Id))?.CurrentCash ?? user.CurrentCash;

                    if (userSettings?.NegativeLimit != null && cash < -userSettings.NegativeLimit)
                    {
                        _logger.LogWarning(
                            "Margin call for user {UserId}: cash {Cash} < limit {Limit}",
                            user.Id, cash, -userSettings.NegativeLimit);
                        var holdings = await _holdingRepo.GetByUserIdAsync(user.Id);
                        foreach (var holding in holdings)
                        {
                            try
                            {
                                // D16: options holdings (users 4-7) square off via the
                                // contract-keyed options path; equities via the legacy one.
                                if (holding.InstrumentType == "EQ")
                                {
                                    await _portfolioService.ExecuteSellAsync(user.Id, holding.Symbol, holding.Quantity);
                                }
                                else if (holding.Expiry is not null && holding.Strike is not null)
                                {
                                    await _portfolioService.ExecuteOptionsSellAsync(
                                        user.Id, holding.Symbol, holding.InstrumentType,
                                        holding.Expiry.Value, holding.Strike.Value, holding.Quantity);
                                }
                            }
                            catch (Exception ex)
                            {
                                // D1: one un-sellable holding (bad quote, missing
                                // contract) must not strand the rest of the user's
                                // liquidation or the other users' margin calls.
                                _logger.LogError(ex,
                                    "Margin-call sell failed for user {UserId} holding {Symbol}, continuing",
                                    user.Id, holding.Symbol);
                            }
                        }

                        // F12: terminal rule. If the liquidation did not recover
                        // the deficit (cash still below the negative limit), the
                        // agent is dead: halt it so it stops trading and stops
                        // accruing interest on a permanent deficit. Admin revive
                        // only - there is no automatic recovery.
                        var postLiquidation = (await _userRepo.GetByIdAsync(user.Id))?.CurrentCash ?? cash;
                        if (postLiquidation < -userSettings.NegativeLimit)
                        {
                            _logger.LogError(
                                "User {UserId} failed liquidation: cash {Cash} still below limit {Limit}; halting agent",
                                user.Id, postLiquidation, -userSettings.NegativeLimit);
                            userSettings.IsHalted = true;
                            await _settingsRepo.UpdateAsync(userSettings);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // D1: per-user isolation for the interest/margin-call step.
                    _logger.LogError(ex,
                        "Interest/margin-call step failed for user {UserId}, continuing",
                        user.Id);
                }
            }

            // 6. PnL snapshots
            foreach (var user in users)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await _portfolioService.RecordDailySnapshotAsync(user.Id);
                }
                catch (Exception ex)
                {
                    // D1: a snapshot failure for one user must not skip the rest.
                    _logger.LogError(ex,
                        "PnL snapshot failed for user {UserId}, continuing",
                        user.Id);
                }
            }

            // A Python failure is not an exception: RunForAllUsersAsync returns
            // a summary with errors. Report it honestly instead of SUCCEEDED.
            run.Status = summary.AllSucceeded ? "SUCCEEDED" : "FAILED";
            run.Error = summary.AllSucceeded ? null : string.Join("; ", summary.Errors);
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

        // D11.2: fire-and-forget desktop notification. Optional: skipped when
        // the script path is unset or plyer is missing. Never blocks or fails
        // the cycle - the toast is a convenience, not a dependency.
        var duration = (DateTime.UtcNow - startedAt).TotalSeconds;
        await SendNotificationAsync(run.Status, duration, run.SignalsGenerated,
                                    run.SignalsExecuted, ct);

        return new DailyCycleRunResult(run.Status, run.UsersProcessed, run.SignalsGenerated, run.SignalsExecuted, run.Error);
    }

    private async Task SendNotificationAsync(string status, double duration,
                                             int signals, int trades,
                                             CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pythonSettings.NotificationScriptPath))
        {
            return;
        }

        try
        {
            var arguments = $"\"{_pythonSettings.NotificationScriptPath}\" " +
                            $"--status {status} --duration {duration:F1} " +
                            $"--signals {signals} --trades {trades}";
            await _processRunner.RunAsync(_pythonSettings.PythonPath, arguments,
                                          ct, TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Desktop notification skipped: {Error}", ex.Message);
        }
    }
}
