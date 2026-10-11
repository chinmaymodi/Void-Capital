using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VoidCapital.Api.Modules.Signals.Services;

namespace VoidCapital.Api.Tests.Modules.Signals.Services;

public class PythonBridgeTests
{
    private static PythonSettings MakeSettings() => new()
    {
        PythonPath = @"C:\tools\python.exe",
        ScriptPath = @"C:\pipeline\generate_signals.py"
    };

    private static PythonBridge CreateBridge(Mock<IProcessRunner> mockRunner, PythonSettings settings) =>
        new(mockRunner.Object, Options.Create(settings), NullLogger<PythonBridge>.Instance);

    [Fact]
    public async Task RunSignalGeneration_ReturnsSuccess_WhenProcessExitsZero()
    {
        var mockRunner = new Mock<IProcessRunner>();
        mockRunner.Setup(r => r.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "Success", ""));

        var bridge = CreateBridge(mockRunner, MakeSettings());
        var result = await bridge.RunSignalGeneration(1);

        Assert.True(result.Success);
        Assert.Equal("Success", result.Output);
    }

    [Fact]
    public async Task RunSignalGeneration_WhenNotConfigured_ReturnsFailureWithClearError()
    {
        var mockRunner = new Mock<IProcessRunner>();
        var settings = new PythonSettings(); // empty paths

        var bridge = CreateBridge(mockRunner, settings);
        var result = await bridge.RunSignalGeneration(1);

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error);
        mockRunner.Verify(r => r.RunAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task RunSignalGeneration_PassesConfiguredPaths()
    {
        var mockRunner = new Mock<IProcessRunner>();
        mockRunner.Setup(r => r.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "", ""));

        var bridge = CreateBridge(mockRunner, MakeSettings());
        await bridge.RunSignalGeneration(2);

        mockRunner.Verify(r => r.RunAsync(
            @"C:\tools\python.exe",
            $"\"C:\\pipeline\\generate_signals.py\" --user 2",
            It.IsAny<CancellationToken>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task RunSignalGeneration_WhenProcessExitsNonZero_ReturnsFailureWithError()
    {
        var mockRunner = new Mock<IProcessRunner>();
        mockRunner.Setup(r => r.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((1, "", "boom"));

        var bridge = CreateBridge(mockRunner, MakeSettings());
        var result = await bridge.RunSignalGeneration(1);

        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
    }

    [Fact]
    public async Task RunSignalGeneration_ExitZeroWithNoOutput_StillSucceeds()
    {
        // Exit 0 with empty output is a possible silent no-op (logged), but
        // the result is still a success - the caller decides what to do.
        var mockRunner = new Mock<IProcessRunner>();
        mockRunner.Setup(r => r.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "", ""));

        var bridge = CreateBridge(mockRunner, MakeSettings());
        var result = await bridge.RunSignalGeneration(1);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task RunDataRefresh_PassesConfiguredPaths()
    {
        var mockRunner = new Mock<IProcessRunner>();
        mockRunner.Setup(r => r.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "refreshed", ""));

        var settings = MakeSettings();
        settings.RefreshScriptPath = @"C:\pipeline\refresh_features.py";
        var bridge = CreateBridge(mockRunner, settings);
        var result = await bridge.RunDataRefreshAsync();

        Assert.True(result.Success);
        Assert.Equal("refreshed", result.Output);
        mockRunner.Verify(r => r.RunAsync(
            @"C:\tools\python.exe",
            "\"C:\\pipeline\\refresh_features.py\"",
            It.IsAny<CancellationToken>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task RunDataRefresh_NotConfigured_ReturnsFailure()
    {
        var mockRunner = new Mock<IProcessRunner>();
        var bridge = CreateBridge(mockRunner, new PythonSettings());

        var result = await bridge.RunDataRefreshAsync();

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error);
        mockRunner.Verify(r => r.RunAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task RunFoIngestion_PassesConfiguredPathsWithDaysBack()
    {
        var mockRunner = new Mock<IProcessRunner>();
        mockRunner.Setup(r => r.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "ingested", ""));

        var settings = MakeSettings();
        settings.FoBhavcopyScriptPath = @"C:\pipeline\ingest_fo.py";
        var bridge = CreateBridge(mockRunner, settings);
        var result = await bridge.RunFoIngestionAsync();

        Assert.True(result.Success);
        Assert.Equal("ingested", result.Output);
        mockRunner.Verify(r => r.RunAsync(
            @"C:\tools\python.exe",
            "\"C:\\pipeline\\ingest_fo.py\" --days-back 7",
            It.IsAny<CancellationToken>(),
            It.IsAny<TimeSpan?>()), Times.Once);
    }

    [Fact]
    public async Task RunFoIngestion_NotConfigured_ReturnsFailure()
    {
        var mockRunner = new Mock<IProcessRunner>();
        var bridge = CreateBridge(mockRunner, new PythonSettings());

        var result = await bridge.RunFoIngestionAsync();

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error);
        mockRunner.Verify(r => r.RunAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Never);
    }
}
