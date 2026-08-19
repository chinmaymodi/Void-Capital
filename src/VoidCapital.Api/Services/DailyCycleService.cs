using Microsoft.Extensions.Hosting;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Services;

/// <summary>
/// Background service that runs the daily cycle once per day at 6PM IST
/// (12:30 UTC). The actual cycle steps live in <see cref="IDailyCycleRunner"/>
/// (scoped); this class is only the scheduler, so it stays trivially testable
/// and the runner can be invoked directly by the admin manual-trigger endpoint.
///
/// Catch-up behavior: on startup, if the last recorded run (ops.cycle_runs)
/// finished before the most recent scheduled slot -- the slot was missed, e.g.
/// the machine was off, the service was stopped, or the previous run crashed
/// mid-flight -- the cycle runs immediately once, then resumes the 24h
/// schedule. A run that already happened after the slot suppresses catch-up,
/// so restarts never double-fire a served slot.
/// </summary>
public class DailyCycleService : BackgroundService
{
    // 6PM IST = 12:30 PM UTC (IST is UTC+5:30)
    internal static readonly TimeSpan SlotUtc = new(12, 30, 0);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DailyCycleService> _logger;

    public DailyCycleService(IServiceProvider serviceProvider, ILogger<DailyCycleService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>Most recent scheduled slot at or before <paramref name="now"/>.</summary>
    public static DateTime LastScheduledSlotUtc(DateTime now)
    {
        var slot = now.Date + SlotUtc;
        return now >= slot ? slot : slot.AddDays(-1);
    }

    /// <summary>True when the last run did not cover the given scheduled slot.</summary>
    /// <remarks>
    /// F14: a FAILED run is treated as unserved. The runner's finally block
    /// always stamps FinishedAt - even on FAILED runs (Python pipeline
    /// failure) - so a status-blind FinishedAt comparison would make the
    /// failed slot look served and permanently suppress catch-up: the day's
    /// trading silently drops with only a log line. FAILED forces a re-fire
    /// on the next startup.
    /// </remarks>
    public static bool NeedsCatchUp(DateTime lastScheduledUtc, DateTime? lastFinishedAtUtc, string? lastStatus = null) =>
        lastFinishedAtUtc is null ||
        lastStatus == "FAILED" ||
        lastFinishedAtUtc.Value < lastScheduledUtc;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCatchUpIfMissedAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTime.UtcNow;
            var target = LastScheduledSlotUtc(now).AddDays(1); // next slot is always ahead

            await Task.Delay(target - now, stoppingToken);
            var status = await RunCycleAsync(stoppingToken);

            // F14: one same-process re-fire for a failed slot. The catch-up
            // heuristic treats FAILED as unserved, but that only helps on
            // restart; this gives a transient Python failure one immediate
            // retry after a short backoff so the day's trading is not
            // silently dropped. SKIPPED (lock held by another instance) is
            // not a failure and never re-fires.
            if (status == "FAILED")
            {
                _logger.LogWarning("Daily cycle failed; retrying once in 30 minutes");
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                await RunCycleAsync(stoppingToken);
            }
        }
    }

    private async Task RunCatchUpIfMissedAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var cycleRepo = scope.ServiceProvider.GetRequiredService<ICycleRunRepository>();
            var lastRun = (await cycleRepo.GetRecentAsync(1)).FirstOrDefault();

            // A run stuck in RUNNING across a restart (e.g. a pre-fix hang with
            // no timeout) would otherwise block catch-up forever. Any legit run
            // is bounded by per-step timeouts and finishes well within 3 hours.
            if (lastRun is { Status: "RUNNING" } &&
                lastRun.StartedAt < DateTime.UtcNow.AddHours(-3))
            {
                lastRun.Status = "FAILED";
                lastRun.Error = $"Aborted on startup: run stuck in RUNNING since {lastRun.StartedAt:O}";
                // Leave FinishedAt null: the stuck run never completed. F14
                // also treats FAILED as unserved, so catch-up fires either way.
                lastRun.FinishedAt = null;
                await cycleRepo.UpdateAsync(lastRun);
                _logger.LogWarning("Marked stale RUNNING cycle run {RunId} as FAILED", lastRun.Id);
            }

            var lastScheduled = LastScheduledSlotUtc(DateTime.UtcNow);
            var missed = NeedsCatchUp(lastScheduled, lastRun?.FinishedAt, lastRun?.Status);

            if (!missed)
            {
                _logger.LogInformation(
                    "Daily cycle up to date (last run {LastRun:O}), next slot {NextSlot:O}",
                    lastRun?.FinishedAt, lastScheduled.AddDays(1));
                return;
            }

            _logger.LogWarning(
                "Missed scheduled slot {Slot:O} (last run {LastRun:O}); running catch-up cycle now",
                lastScheduled, lastRun?.FinishedAt);
            await RunCycleAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            // DB unreachable or migrations not applied: fall back to the plain
            // schedule rather than crashing the host.
            _logger.LogWarning(ex, "Catch-up check failed; falling back to normal schedule");
        }
    }

    private async Task<string?> RunCycleAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IDailyCycleRunner>();
            var run = await runner.RunAsync(stoppingToken);
            _logger.LogInformation(
                "Daily cycle {Status}: users={Users}, signals={Generated}, executed={Executed}, error={Error}",
                run.Status, run.UsersProcessed, run.SignalsGenerated, run.SignalsExecuted, run.Error);
            return run.Status;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Daily cycle cancelled during shutdown");
            return null;
        }
        catch (Exception ex)
        {
            // A crash before the runner recorded a run row is a failure too:
            // it never served the slot, so the caller may re-fire.
            _logger.LogError(ex, "Daily cycle crashed");
            return "FAILED";
        }
    }
}
