using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VoidCapital.Api.Modules.Signals.Services;

public class PythonBridge : IPythonBridge
{
    private readonly IProcessRunner _runner;
    private readonly PythonSettings _settings;
    private readonly ILogger<PythonBridge> _logger;

    public PythonBridge(
        IProcessRunner runner,
        IOptions<PythonSettings> options,
        ILogger<PythonBridge> logger)
    {
        _runner = runner;
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<PythonRunResult> RunSignalGeneration(
        int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.PythonPath) ||
            string.IsNullOrWhiteSpace(_settings.ScriptPath))
        {
            return new PythonRunResult(false, "", "Python bridge is not configured (Python:PythonPath / Python:ScriptPath).");
        }

        var arguments = $"\"{_settings.ScriptPath}\" --user {userId}";
        var (exitCode, output, error) = await _runner.RunAsync(_settings.PythonPath, arguments, ct);
        if (exitCode == 0 && string.IsNullOrWhiteSpace(output))
        {
            _logger.LogWarning(
                "Signal generation for user {UserId} exited 0 but produced no output - possible silent no-op.",
                userId);
        }
        return new PythonRunResult(exitCode == 0, output, error);
    }

    public async Task<PythonRunResult> RunDataRefreshAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.PythonPath) ||
            string.IsNullOrWhiteSpace(_settings.RefreshScriptPath))
        {
            return new PythonRunResult(false, "", "Python bridge is not configured (Python:PythonPath / Python:RefreshScriptPath).");
        }

        var arguments = $"\"{_settings.RefreshScriptPath}\"";
        var (exitCode, output, error) = await _runner.RunAsync(_settings.PythonPath, arguments, ct);
        return new PythonRunResult(exitCode == 0, output, error);
    }
}
