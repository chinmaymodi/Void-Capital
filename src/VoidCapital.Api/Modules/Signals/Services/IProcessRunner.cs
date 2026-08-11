using System.Diagnostics;

namespace VoidCapital.Api.Modules.Signals.Services;

public interface IProcessRunner
{
    /// <summary>
    /// Runs an external process and captures stdout/stderr.
    /// <paramref name="timeout"/> bounds the whole invocation; on timeout or
    /// cancellation the child process tree is killed.
    /// </summary>
    Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string arguments,
        CancellationToken ct = default,
        TimeSpan? timeout = null);
}

public class ProcessRunner : IProcessRunner
{
    /// <summary>Upper bound per Python signal-generation call.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    public async Task<(int ExitCode, string Output, string Error)> RunAsync(
        string fileName, string arguments,
        CancellationToken ct = default,
        TimeSpan? timeout = null)
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

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            // Drain stdout and stderr CONCURRENTLY. Reading stdout to EOF
            // before touching stderr deadlocks once the child fills the
            // anonymous-pipe buffer (~4KB) with stderr writes: the child
            // blocks on stderr, never exits, stdout never reaches EOF.
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            await process.WaitForExitAsync(cts.Token);

            var output = await outputTask;
            var error = await errorTask;
            return (process.ExitCode, output, error);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Our deadline fired while the caller is still alive: kill the
            // child and report failure instead of hanging the daily cycle.
            KillTree(process);
            return (1, "",
                $"External process timed out after {timeout ?? DefaultTimeout} and was terminated.");
        }
        catch (OperationCanceledException)
        {
            // Host shutdown: kill the child so `sc stop` completes cleanly.
            KillTree(process);
            throw;
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited; nothing to kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Access denied on kill; the timeout already reported failure.
        }
    }
}
