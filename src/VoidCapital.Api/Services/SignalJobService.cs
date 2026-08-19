using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VoidCapital.Api.Modules.Signals.Services;

namespace VoidCapital.Api.Services;

/// <summary>Status of one async signal-generation job.</summary>
public enum SignalJobStatus
{
    RUNNING,
    SUCCEEDED,
    FAILED
}

/// <summary>In-memory record of an async signal-generation job.</summary>
public class SignalJob
{
    public int JobId { get; init; }
    public SignalJobStatus Status { get; set; } = SignalJobStatus.RUNNING;
    public DateTime StartedAt { get; init; }
    public DateTime? FinishedAt { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// Runs signal generation as a background job so the HTTP call returns
/// immediately (the Python pipeline takes 1-2 minutes per user, far beyond
/// the frontend's 15s axios timeout). Callers poll <see cref="Get"/> until
/// the job leaves RUNNING. Jobs live in memory and are pruned to the newest
/// <see cref="SignalJobService.MaxJobs"/>.
/// </summary>
public interface ISignalJobService
{
    SignalJob Start();
    SignalJob? Get(int jobId);
}

public class SignalJobService : ISignalJobService
{
    internal const int MaxJobs = 20;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SignalJobService> _logger;
    private readonly ConcurrentDictionary<int, SignalJob> _jobs = new();
    private int _nextId;
    private int _isRunning; // 0 for false, 1 for true

    public SignalJobService(IServiceScopeFactory scopeFactory, ILogger<SignalJobService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public SignalJob Start()
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            throw new InvalidOperationException("A signal generation job is already running.");
        }

        var job = new SignalJob
        {
            JobId = Interlocked.Increment(ref _nextId),
            StartedAt = DateTime.UtcNow
        };
        _jobs[job.JobId] = job;
        Prune();

        _ = Task.Run(async () =>
        {
            try
            {
                await RunAsync(job);
            }
            finally
            {
                Interlocked.Exchange(ref _isRunning, 0);
            }
        });
        return job;
    }

    public SignalJob? Get(int jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    private async Task RunAsync(SignalJob job)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var integration = scope.ServiceProvider.GetRequiredService<ISignalIntegrationService>();
            var summary = await integration.RunForAllUsersAsync();

            job.Status = summary.AllSucceeded ? SignalJobStatus.SUCCEEDED : SignalJobStatus.FAILED;
            job.Message = summary.AllSucceeded
                ? $"Signal generation complete: {summary.UsersSucceeded} user(s), 0 failures"
                : $"Signal generation failed for {summary.Errors.Count} user(s): {string.Join("; ", summary.Errors)}";
        }
        catch (Exception ex)
        {
            job.Status = SignalJobStatus.FAILED;
            job.Message = ex.Message;
            _logger.LogError(ex, "Signal generation job {JobId} crashed", job.JobId);
        }
        finally
        {
            job.FinishedAt = DateTime.UtcNow;
        }
    }

    private void Prune()
    {
        var stale = _jobs.Values
            .OrderByDescending(j => j.JobId)
            .Skip(MaxJobs)
            .Select(j => j.JobId)
            .ToList();
        foreach (var id in stale)
            _jobs.TryRemove(id, out _);
    }
}