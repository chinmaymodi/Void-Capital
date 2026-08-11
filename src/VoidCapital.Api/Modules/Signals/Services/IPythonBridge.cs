namespace VoidCapital.Api.Modules.Signals.Services;

public record PythonRunResult(bool Success, string Output, string Error);

public interface IPythonBridge
{
    Task<PythonRunResult> RunSignalGeneration(int userId, bool noGate, CancellationToken ct = default);

    /// <summary>Run the daily feature refresh (refresh_daily.py, D1 step 0).</summary>
    Task<PythonRunResult> RunDataRefreshAsync(CancellationToken ct = default);
}