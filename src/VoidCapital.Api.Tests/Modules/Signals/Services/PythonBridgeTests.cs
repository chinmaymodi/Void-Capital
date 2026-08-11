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

    [Fact]
    public async Task RunSignalGeneration_ReturnsSuccess_WhenProcessExitsZero()
    {
        var mockRunner = new Mock<IProcessRunner>();
        mockRunner.Setup(r => r.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "Success", ""));

        var bridge = new PythonBridge(mockRunner.Object, Options.Create(MakeSettings()));
        var result = await bridge.RunSignalGeneration(1, false);

        Assert.True(result.Success);
        Assert.Equal("Success", result.Output);
    }

    [Fact]
    public async Task RunSignalGeneration_WhenNotConfigured_ReturnsFailureWithClearError()
    {
        var mockRunner = new Mock<IProcessRunner>();
        var settings = new PythonSettings(); // empty paths

        var bridge = new PythonBridge(mockRunner.Object, Options.Create(settings));
        var result = await bridge.RunSignalGeneration(1, false);

        Assert.False(result.Success);
        Assert.Contains("not configured", result.Error);
        mockRunner.Verify(r => r.RunAsync(
            It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()), Times.Never);
    }

    [Fact]
    public async Task RunSignalGeneration_PassesConfiguredPathsAndNoGateFlag()
    {
        var mockRunner = new Mock<IProcessRunner>();
        mockRunner.Setup(r => r.RunAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>(), It.IsAny<TimeSpan?>()))
            .ReturnsAsync((0, "", ""));

        var bridge = new PythonBridge(mockRunner.Object, Options.Create(MakeSettings()));
        await bridge.RunSignalGeneration(2, noGate: true);

        mockRunner.Verify(r => r.RunAsync(
            @"C:\tools\python.exe",
            $"\"C:\\pipeline\\generate_signals.py\" --user 2 --no-gate",
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

        var bridge = new PythonBridge(mockRunner.Object, Options.Create(MakeSettings()));
        var result = await bridge.RunSignalGeneration(1, false);

        Assert.False(result.Success);
        Assert.Equal("boom", result.Error);
    }
}
