using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

public class SignalsControllerTests
{
    private readonly Mock<ISignalService> _signalService = new();
    private SignalsController CreateController() => new(_signalService.Object);

    private static SignalDto MakeDto(int id = 1, string status = "PENDING") => new(
        Id: id,
        Date: new DateOnly(2026, 8, 1),
        Symbol: "RELIANCE",
        Action: "BUY",
        Confidence: 0.75m,
        Reason: "SMA crossover",
        ModelName: "sma",
        Status: status,
        SuggestedQuantity: 10,
        EntryPrice: 2860m,
        TargetPrice: 3000m,
        StopLoss: 2700m);

    [Fact]
    public async Task GetToday_ReturnsSignalsForUser()
    {
        _signalService.Setup(s => s.GetTodaySignalsAsync(1))
            .ReturnsAsync(new[] { MakeDto() });

        var result = await CreateController().GetToday(1);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<SignalDto>>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().ContainSingle();
    }

    [Fact]
    public async Task Approve_DelegatesToServiceAndReturnsDto()
    {
        _signalService.Setup(s => s.ApproveSignalAsync(5)).ReturnsAsync(MakeDto(status: "APPROVED"));

        var result = await CreateController().Approve(5);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SignalDto>>().Subject;
        envelope.Data!.Status.Should().Be("APPROVED");
        _signalService.Verify(s => s.ApproveSignalAsync(5), Times.Once);
    }

    [Fact]
    public async Task Reject_DelegatesToServiceAndReturnsDto()
    {
        _signalService.Setup(s => s.RejectSignalAsync(5)).ReturnsAsync(MakeDto(status: "REJECTED"));

        var result = await CreateController().Reject(5);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SignalDto>>().Subject;
        envelope.Data!.Status.Should().Be("REJECTED");
        _signalService.Verify(s => s.RejectSignalAsync(5), Times.Once);
    }

    [Fact]
    public async Task BatchApprove_PassesIdsToService()
    {
        _signalService.Setup(s => s.BatchApproveAsync(It.IsAny<int[]>()))
            .ReturnsAsync(new[] { SignalBatchResult.Ok(1), SignalBatchResult.Ok(2) });

        var result = await CreateController().BatchApprove(new BatchSignalRequest(new[] { 1, 2 }));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<SignalBatchResult>>>().Subject;
        envelope.Data.Should().HaveCount(2);
        _signalService.Verify(s => s.BatchApproveAsync(new[] { 1, 2 }), Times.Once);
    }

    [Fact]
    public async Task BatchReject_PassesIdsToService()
    {
        _signalService.Setup(s => s.BatchRejectAsync(It.IsAny<int[]>()))
            .ReturnsAsync(new[] { SignalBatchResult.Failed(1, "already processed") });

        var result = await CreateController().BatchReject(new BatchSignalRequest(new[] { 1 }));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<SignalBatchResult>>>().Subject;
        envelope.Data!.Single().Success.Should().BeFalse();
        _signalService.Verify(s => s.BatchRejectAsync(new[] { 1 }), Times.Once);
    }
}
