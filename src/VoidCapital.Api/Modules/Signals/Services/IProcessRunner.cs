using System.Diagnostics;

namespace VoidCapital.Api.Modules.Signals.Services;

public interface IProcessRunner
{
    Task<(int ExitCode, string Output, string Error)> RunAsync(string fileName, string arguments);
}

public class ProcessRunner : IProcessRunner
{
    public async Task<(int ExitCode, string Output, string Error)> RunAsync(string fileName, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, output, error);
    }
}