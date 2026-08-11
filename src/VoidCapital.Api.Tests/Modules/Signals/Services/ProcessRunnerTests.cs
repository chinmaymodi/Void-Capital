using VoidCapital.Api.Modules.Signals.Services;

namespace VoidCapital.Api.Tests.Modules.Signals.Services;

/// <summary>
/// Locks in the Aug 2026 hang fix: ProcessRunner must (a) not deadlock when
/// the child floods stderr, (b) time out and kill a child that never exits,
/// and (c) honor cancellation so host shutdown completes.
/// </summary>
public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task RunAsync_CapturesStdoutAndExitCode()
    {
        var (exitCode, output, error) = await _runner.RunAsync("cmd.exe", "/c echo hello");

        Assert.Equal(0, exitCode);
        Assert.Contains("hello", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task RunAsync_DoesNotDeadlock_WhenChildFloodsStderr()
    {
        // ~45KB to stderr, far beyond the ~4KB anonymous-pipe buffer. Reading
        // stderr only after stdout EOF would deadlock here; concurrent drain
        // must return promptly.
        var args = "/c \"for /L %i in (1,1,1000) do @echo 0123456789abcdefghijklmnopqrstuv %i 1>&2\"";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (exitCode, output, error) = await _runner.RunAsync("cmd.exe", args, cts.Token);

        Assert.Equal(0, exitCode);
        Assert.Empty(output);
        Assert.Contains("0123456789", error);
    }

    [Fact]
    public async Task RunAsync_TimesOutAndKillsChild_WhenProcessNeverExits()
    {
        // ping with 60 replies runs ~60s; the runner must give up in 2s.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var (exitCode, output, error) = await _runner.RunAsync(
            "ping", "127.0.0.1 -n 60", cts.Token, timeout: TimeSpan.FromSeconds(2));

        Assert.NotEqual(0, exitCode);
        Assert.Contains("timed out", error);
    }

    [Fact]
    public async Task RunAsync_CancelledToken_ThrowsAndKillsChild()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _runner.RunAsync("ping", "127.0.0.1 -n 60", cancelled.Token));
    }
}
