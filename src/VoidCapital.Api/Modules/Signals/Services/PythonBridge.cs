using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace VoidCapital.Api.Modules.Signals.Services;

public class PythonBridge : IPythonBridge
{
    private readonly IProcessRunner _runner;
    private readonly PythonSettings _settings;

    public PythonBridge(IProcessRunner runner, IOptions<PythonSettings> options)
    {
        _runner = runner;
        _settings = options.Value;
    }

    public async Task<PythonRunResult> RunSignalGeneration(
        int userId, bool noGate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.PythonPath) ||
            string.IsNullOrWhiteSpace(_settings.ScriptPath))
        {
            return new PythonRunResult(false, "", "Python bridge is not configured (Python:PythonPath / Python:ScriptPath).");
        }

        var arguments = $"\"{_settings.ScriptPath}\" --user {userId} {(noGate ? "--no-gate" : "")}";
        var (exitCode, output, error) = await _runner.RunAsync(_settings.PythonPath, arguments, ct);
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
