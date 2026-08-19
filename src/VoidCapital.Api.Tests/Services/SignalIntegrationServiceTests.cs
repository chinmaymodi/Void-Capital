using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Services;

public class SignalIntegrationServiceTests
{
    private readonly Mock<IPythonBridge> _bridge = new();
    private readonly Mock<ISettingsRepository> _settingsRepo = new();

    private SignalIntegrationService CreateService() => new(
        _bridge.Object,
        _settingsRepo.Object,
        NullLogger<SignalIntegrationService>.Instance);

    private static UserSettings MakeSettings(int userId) => new()
    {
        Id = userId,
        UserId = userId,
        Watchlist = "[]"
    };

    private static PythonRunResult Ok() => new(true, "0", "");
    private static PythonRunResult Fail(string error = "boom") => new(false, "", error);

    [Fact]
    public async Task RunForAllUsers_NoSettings_ReturnsEmptySummary()
    {
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<UserSettings>());

        var summary = await CreateService().RunForAllUsersAsync();

        summary.UsersProcessed.Should().Be(0);
        summary.UsersSucceeded.Should().Be(0);
        summary.AllSucceeded.Should().BeTrue();
        _bridge.Verify(b => b.RunSignalGeneration(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunForAllUsers_AllSucceed_ReturnsSuccess()
    {
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            MakeSettings(1), MakeSettings(2)
        });
        _bridge.Setup(b => b.RunSignalGeneration(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());

        var summary = await CreateService().RunForAllUsersAsync();

        summary.UsersProcessed.Should().Be(2);
        summary.UsersSucceeded.Should().Be(2);
        summary.AllSucceeded.Should().BeTrue();
        summary.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task RunForAll_OneFails_RetriesThenReportsError()
    {
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            MakeSettings(1)
        });
        _bridge.Setup(b => b.RunSignalGeneration(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fail("python crashed"));

        var summary = await CreateService().RunForAllUsersAsync();

        summary.UsersSucceeded.Should().Be(0);
        summary.AllSucceeded.Should().BeFalse();
        summary.Errors.Should().ContainSingle(e => e.Contains("user 1") && e.Contains("python crashed"));
        // 2 attempts: 1 + 1 backoff retry.
        _bridge.Verify(b => b.RunSignalGeneration(1, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunForAll_FailsThenSucceeds_RetriesUntilSuccess()
    {
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { MakeSettings(1) });
        _bridge.SetupSequence(b => b.RunSignalGeneration(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fail("transient"))
            .ReturnsAsync(Ok());

        var summary = await CreateService().RunForAllUsersAsync();

        summary.UsersSucceeded.Should().Be(1);
        summary.AllSucceeded.Should().BeTrue();
        _bridge.Verify(b => b.RunSignalGeneration(1, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunForAll_MixedResults_CountsCorrectly()
    {
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            MakeSettings(1), MakeSettings(2), MakeSettings(3)
        });
        _bridge.Setup(b => b.RunSignalGeneration(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());
        _bridge.Setup(b => b.RunSignalGeneration(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Fail("nope"));
        _bridge.Setup(b => b.RunSignalGeneration(3, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Ok());

        var summary = await CreateService().RunForAllUsersAsync();

        summary.UsersProcessed.Should().Be(3);
        summary.UsersSucceeded.Should().Be(2);
        summary.AllSucceeded.Should().BeFalse();
        summary.Errors.Should().ContainSingle(e => e.Contains("user 2"));
    }

    [Fact]
    public async Task RunForAll_CancellationRequested_Throws()
    {
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { MakeSettings(1) });
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await CreateService().RunForAllUsersAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}