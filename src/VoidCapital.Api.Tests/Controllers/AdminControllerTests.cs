using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Services;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

/// <summary>
/// A3: AdminController is a thin HTTP mapping layer over IAdminService. These
/// tests verify the mapping (envelope wrapping, status codes, exception
/// propagation); the orchestration logic lives in AdminServiceTests.
/// </summary>
public class AdminControllerTests
{
    private readonly Mock<IAdminService> _admin = new();

    private AdminController CreateController() => new(_admin.Object);

    private static SettingsDto MakeSettingsDto() => new(
        Id: 1, UserId: 2, AutoExecute: true, IsHalted: false, MinConfidence: 0.5m,
        NegativeLimit: 100000m, InterestRate: 0.0005m, Watchlist: new[] { "RELIANCE", "TCS" });

    private static SignalDto MakeSignalDto() => new(
        Id: 1, Date: new DateOnly(2026, 8, 17), Symbol: "RELIANCE", Action: "BUY",
        Confidence: 0.75m, Reason: "SMA crossover", ModelName: "sma",
        Status: "PENDING", SuggestedQuantity: 10, EntryPrice: 2860m,
        TargetPrice: 3000m, StopLoss: 2700m, FailureReason: null);

    private static IngestSignalRequest MakeRequest() => new(
        UserId: 1, Symbol: "RELIANCE", Action: "BUY", Confidence: 0.75m,
        Reason: "SMA crossover bullish", ModelName: "sma", SuggestedQuantity: 10,
        EntryPrice: 2860m, TargetPrice: 3000m, StopLoss: 2700m);

    [Fact]
    public async Task IngestSignals_ReturnsServiceResult()
    {
        _admin.Setup(a => a.IngestSignalsAsync(It.IsAny<IEnumerable<IngestSignalRequest>>()))
            .ReturnsAsync(new[] { MakeSignalDto() });

        var result = await CreateController().IngestSignals(new[] { MakeRequest() }, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<SignalDto>>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task GetSettings_ReturnsServiceResult()
    {
        _admin.Setup(a => a.GetSettingsAsync(2)).ReturnsAsync(MakeSettingsDto());

        var result = await CreateController().GetSettings(2, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SettingsDto>>().Subject;
        envelope.Data!.UserId.Should().Be(2);
    }

    [Fact]
    public async Task GetSettings_WhenServiceThrowsNotFound_Propagates()
    {
        _admin.Setup(a => a.GetSettingsAsync(99))
            .ThrowsAsync(new NotFoundException("Settings for user 99 were not found."));

        var act = () => CreateController().GetSettings(99, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateSettings_ReturnsServiceResult()
    {
        _admin.Setup(a => a.UpdateSettingsAsync(2, It.IsAny<UpdateSettingsRequest>()))
            .ReturnsAsync(MakeSettingsDto() with { NegativeLimit = 200000m });

        var result = await CreateController().UpdateSettings(
            2, new UpdateSettingsRequest(true, 0.5m, 200000m, 0.0005m, new[] { "INFY" }), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SettingsDto>>().Subject;
        envelope.Data!.NegativeLimit.Should().Be(200000m);
    }

    [Fact]
    public async Task UpdateGlobalSettings_ReturnsServiceResult()
    {
        _admin.Setup(a => a.UpdateGlobalSettingsAsync(It.IsAny<GlobalSettingsRequest>()))
            .ReturnsAsync(new[] { MakeSettingsDto() });

        var result = await CreateController().UpdateGlobalSettings(
            new GlobalSettingsRequest(0.55m, 0m, 0m, new[] { "HDFCBANK" }), CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<SettingsDto>>>().Subject;
        envelope.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task SquareOff_ReturnsServiceResult()
    {
        _admin.Setup(a => a.SquareOffAsync(3))
            .ReturnsAsync(new SquareOffResultDto(3, 2, 48000m, 49000m));

        var result = await CreateController().SquareOff(3, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SquareOffResultDto>>().Subject;
        envelope.Data!.PositionsSold.Should().Be(2);
        envelope.Data.Proceeds.Should().Be(48000m);
    }

    [Fact]
    public async Task GetStatus_ReturnsServiceResult()
    {
        _admin.Setup(a => a.GetStatusAsync())
            .ReturnsAsync(new AdminStatusDto(DateTime.UtcNow, 3, new[]
            {
                new UserBalanceDto(1, "Trader One", 90000m, 110000m, 10000m, 0.1m)
            }));

        var result = await CreateController().GetStatus(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<AdminStatusDto>>().Subject;
        envelope.Data!.PendingSignalCount.Should().Be(3);
        envelope.Data.Users.Should().ContainSingle();
    }

    [Fact]
    public void RunSignals_ReturnsAcceptedWithJob()
    {
        _admin.Setup(a => a.StartSignalJob())
            .Returns(new SignalJobDto(7, "RUNNING", DateTime.UtcNow, null, null));

        var result = CreateController().RunSignals();

        var accepted = result.Result.Should().BeOfType<AcceptedResult>().Subject;
        var envelope = accepted.Value.Should().BeOfType<ApiResponse<SignalJobDto>>().Subject;
        envelope.Data!.JobId.Should().Be(7);
        envelope.Data.Status.Should().Be("RUNNING");
    }

    [Fact]
    public void GetRunSignalsStatus_ReturnsJobStatus()
    {
        _admin.Setup(a => a.GetSignalJob(7))
            .Returns(new SignalJobDto(7, "SUCCEEDED", DateTime.UtcNow.AddMinutes(-5),
                DateTime.UtcNow, "Signal generation complete"));

        var result = CreateController().GetRunSignalsStatus(7);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SignalJobDto>>().Subject;
        envelope.Data!.Status.Should().Be("SUCCEEDED");
    }

    [Fact]
    public void GetRunSignalsStatus_WhenMissing_PropagatesNotFound()
    {
        _admin.Setup(a => a.GetSignalJob(99))
            .Throws(new NotFoundException("Signal generation job 99 was not found."));

        var act = () => CreateController().GetRunSignalsStatus(99);

        act.Should().Throw<NotFoundException>();
    }

    [Fact]
    public async Task RunDailyCycle_ReturnsSummary()
    {
        _admin.Setup(a => a.RunDailyCycleAsync())
            .ReturnsAsync(new DailyCycleRunResult("SUCCEEDED", 7, 12, 4, null));

        var result = await CreateController().RunDailyCycle(CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<string>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().Contain("SUCCEEDED");
        envelope.Data.Should().Contain("7 user(s)");
        envelope.Data.Should().Contain("12 signal run(s)");
        envelope.Data.Should().Contain("4 executed");
    }

    [Fact]
    public async Task RunDailyCycle_FailedRun_Returns500()
    {
        _admin.Setup(a => a.RunDailyCycleAsync())
            .ReturnsAsync(new DailyCycleRunResult("FAILED", 7, 12, 0, "python exploded"));

        var result = await CreateController().RunDailyCycle(CancellationToken.None);

        var status = result.Result.Should().BeOfType<ObjectResult>().Subject;
        status.StatusCode.Should().Be(500);
        var envelope = status.Value.Should().BeOfType<ApiResponse<string>>().Subject;
        envelope.Success.Should().BeFalse();
        envelope.Error.Should().Contain("python exploded");
    }
}