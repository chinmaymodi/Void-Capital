namespace VoidCapital.Api.Modules.Signals.Services;

public record PythonRunResult(bool Success, string Output, string Error);

public interface IPythonBridge
{
    Task<PythonRunResult> RunSignalGeneration(int userId, bool noGate);
}