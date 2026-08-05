using Microsoft.Extensions.Logging;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Modules.Signals.Services;

/// <summary>Aggregated result of one signal-generation sweep across users.</summary>
public record SignalRunSummary(int UsersProcessed, int UsersSucceeded, IReadOnlyList<string> Errors)
{
    public bool AllSucceeded => Errors.Count == 0;
}

/// <summary>
/// Facade over the Python signal-generation pipeline. Iterates every user
/// (from settings rows) and runs signal generation per user with retry +
/// exponential backoff on failure. The controller is a thin HTTP layer over
/// this service (ticket D9: facade pattern).
/// </summary>
public interface ISignalIntegrationService
{
    Task<SignalRunSummary> RunForAllUsersAsync(CancellationToken ct = default);
}

public class SignalIntegrationService : ISignalIntegrationService
{
    private readonly IPythonBridge _pythonBridge;
    private readonly ISettingsRepository _settingsRepo;
    private readonly ILogger<SignalIntegrationService> _logger;

    private const int MaxAttempts = 3;
    private static readonly TimeSpan[] BackoffDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    };

    public SignalIntegrationService(
        IPythonBridge pythonBridge,
        ISettingsRepository settingsRepo,
        ILogger<SignalIntegrationService> logger)
    {
        _pythonBridge = pythonBridge;
        _settingsRepo = settingsRepo;
        _logger = logger;
    }

    public async Task<SignalRunSummary> RunForAllUsersAsync(CancellationToken ct = default)
    {
        var settings = (await _settingsRepo.GetAllAsync()).ToList();
        if (settings.Count == 0)
        {
            _logger.LogWarning("Signal integration: no settings rows, nothing to run.");
            return new SignalRunSummary(0, 0, []);
        }

        var succeeded = 0;
        var errors = new List<string>();

        foreach (var user in settings)
        {
            ct.ThrowIfCancellationRequested();
            var (ok, error) = await RunWithRetryAsync(user.UserId, ct);
            if (ok)
                succeeded++;
            else
                errors.Add($"user {user.UserId}: {error}");
        }

        _logger.LogInformation(
            "Signal integration complete: {Succeeded}/{Processed} users succeeded",
            succeeded, settings.Count);

        return new SignalRunSummary(settings.Count, succeeded, errors);
    }

    private async Task<(bool Ok, string Error)> RunWithRetryAsync(int userId, CancellationToken ct)
    {
        string lastError = "unknown error";
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var result = await _pythonBridge.RunSignalGeneration(userId, noGate: false);

            if (result.Success)
            {
                _logger.LogInformation("Signal generation for user {UserId} succeeded (attempt {Attempt})", userId, attempt);
                return (true, "");
            }

            lastError = result.Error;
            _logger.LogWarning(
                "Signal generation for user {UserId} failed (attempt {Attempt}/{Max}): {Error}",
                userId, attempt, MaxAttempts, lastError);

            if (attempt < MaxAttempts)
            {
                await Task.Delay(BackoffDelays[attempt - 1], ct);
            }
        }

        return (false, lastError);
    }
}
