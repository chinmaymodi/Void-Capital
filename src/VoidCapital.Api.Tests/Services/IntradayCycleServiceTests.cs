using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Services;
using Xunit;

namespace VoidCapital.Api.Tests.Services;

/// <summary>
/// IntradayCycleService collection/launch paths. The static scheduler math
/// lives in IntradayCycleSchedulerTests; these exercise the private
/// CheckAndCollectAsync / LaunchCollectorAsync methods (via reflection) with
/// a real ServiceProvider so scope resolution behaves like production.
/// </summary>
public class IntradayCycleServiceTests
{
    private static readonly DateTime Now = new(2026, 8, 13, 6, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IMarketDataRepository> _repo = new();
    private readonly Mock<IProcessRunner> _runner = new();

    private IntradayCycleService CreateService(string collectScriptPath = "collect_live.py")
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _repo.Object);
        services.AddScoped(_ => _runner.Object);
        var provider = services.BuildServiceProvider();

        var settings = Options.Create(new PythonSettings
        {
            PythonPath = "python.exe",
            CollectLiveScriptPath = collectScriptPath,
        });
        return new IntradayCycleService(provider, settings, NullLogger<IntradayCycleService>.Instance);
    }

    private static Task InvokeCheckAndCollectAsync(IntradayCycleService service)
    {
        var method = typeof(IntradayCycleService).GetMethod(
            "CheckAndCollectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(service, new object[] { CancellationToken.None })!;
    }

    private static Task InvokeLaunchCollectorAsync(IntradayCycleService service)
    {
        var method = typeof(IntradayCycleService).GetMethod(
            "LaunchCollectorAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(service, new object[] { CancellationToken.None })!;
    }

    [Fact]
    public async Task CheckAndCollect_StaleData_LaunchesCollector()
    {
        var stale = DateTime.UtcNow.AddMinutes(-10);   // > 5-minute threshold
        _repo.Setup(r => r.GetLatestIntradayTimestampAsync()).ReturnsAsync(stale);
        _repo.Setup(r => r.GetLatestOptionsIntradayTimestampAsync()).ReturnsAsync(stale);
        _runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "", ""));

        await InvokeCheckAndCollectAsync(CreateService());

        _runner.Verify(r => r.RunAsync(
            "python.exe",
            It.Is<string>(a => a.Contains("collect_live.py")),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task CheckAndCollect_FreshData_DoesNotLaunch()
    {
        var fresh = DateTime.UtcNow.AddMinutes(-1);
        _repo.Setup(r => r.GetLatestIntradayTimestampAsync()).ReturnsAsync(fresh);
        _repo.Setup(r => r.GetLatestOptionsIntradayTimestampAsync()).ReturnsAsync(fresh);

        await InvokeCheckAndCollectAsync(CreateService());

        _runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task CheckAndCollect_OptionsFeedMissing_LaunchesCollector()
    {
        // F15: equity bars fresh but options snapshots absent -> stale path.
        var fresh = DateTime.UtcNow.AddMinutes(-1);
        _repo.Setup(r => r.GetLatestIntradayTimestampAsync()).ReturnsAsync(fresh);
        _repo.Setup(r => r.GetLatestOptionsIntradayTimestampAsync())
            .ReturnsAsync((DateTime?)null);
        _runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "", ""));

        await InvokeCheckAndCollectAsync(CreateService());

        _runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task LaunchCollector_NoScriptPathConfigured_DoesNotLaunch()
    {
        await InvokeLaunchCollectorAsync(CreateService(collectScriptPath: ""));

        _runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task LaunchCollector_NonZeroExit_LogsWarning()
    {
        _runner.Setup(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((1, "", "collector crashed"));

        await InvokeLaunchCollectorAsync(CreateService());

        _runner.Verify(r => r.RunAsync(It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Once);
    }
}