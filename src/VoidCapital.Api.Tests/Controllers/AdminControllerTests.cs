using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

public class AdminControllerTests
{
    private readonly Mock<ISignalRepository> _signalRepo = new();
    private readonly Mock<ISignalPerformanceRepository> _perfRepo = new();
    private AdminController CreateController() => new(_signalRepo.Object, _perfRepo.Object);

    private static IngestSignalRequest MakeRequest(int? userId = 1) => new(
        UserId: userId,
        Symbol: "RELIANCE",
        Action: "BUY",
        Confidence: 0.75m,
        Reason: "SMA crossover bullish",
        ModelName: "sma",
        SuggestedQuantity: 10,
        EntryPrice: 2860m,
        TargetPrice: 3000m,
        StopLoss: 2700m);

    [Fact]
    public async Task IngestSignals_CreatesSignalWithUserIdAndQuantity()
    {
        Signal? captured = null;
        _signalRepo
            .Setup(r => r.AddAsync(It.IsAny<Signal>()))
            .Callback<Signal>(s => captured = s)
            .ReturnsAsync((Signal s) => s);

        var result = await CreateController().IngestSignals(new[] { MakeRequest() });

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<SignalDto>>>().Subject;
        envelope.Success.Should().BeTrue();

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(1);
        captured.Symbol.Should().Be("RELIANCE");
        captured.SuggestedQuantity.Should().Be(10);
        captured.Status.Should().Be(SignalStatus.PENDING);
        captured.Date.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    [Fact]
    public async Task IngestSignals_CreatesLinkedPerformanceRow()
    {
        _signalRepo
            .Setup(r => r.AddAsync(It.IsAny<Signal>()))
            .ReturnsAsync((Signal s) => s);

        SignalPerformance? captured = null;
        _perfRepo
            .Setup(r => r.AddAsync(It.IsAny<SignalPerformance>()))
            .Callback<SignalPerformance>(p => captured = p)
            .ReturnsAsync((SignalPerformance p) => p);

        await CreateController().IngestSignals(new[] { MakeRequest() });

        captured.Should().NotBeNull();
        captured!.EntryPrice.Should().Be(2860m);
        captured.TargetPrice.Should().Be(3000m);
        captured.StopLoss.Should().Be(2700m);
        captured.Outcome.Should().Be("PENDING");
        captured.EvaluationDays.Should().Be(5);
    }

    [Fact]
    public async Task IngestSignals_WhenUserIdMissing_ThrowsValidation()
    {
        var controller = CreateController();
        var act = () => controller.IngestSignals(new[] { MakeRequest(userId: null) });

        await act.Should().ThrowAsync<ValidationException>();
        _signalRepo.Verify(r => r.AddAsync(It.IsAny<Signal>()), Times.Never);
        _perfRepo.Verify(r => r.AddAsync(It.IsAny<SignalPerformance>()), Times.Never);
    }

    [Fact]
    public async Task IngestSignals_ProcessesMultipleRequests()
    {
        _signalRepo
            .Setup(r => r.AddAsync(It.IsAny<Signal>()))
            .ReturnsAsync((Signal s) => s);
        _perfRepo
            .Setup(r => r.AddAsync(It.IsAny<SignalPerformance>()))
            .ReturnsAsync((SignalPerformance p) => p);

        var result = await CreateController().IngestSignals(new[]
        {
            MakeRequest(),
            MakeRequest() with { Symbol = "TCS", UserId = 2 }
        });

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<SignalDto>>>().Subject;
        envelope.Data.Should().HaveCount(2);
        _signalRepo.Verify(r => r.AddAsync(It.IsAny<Signal>()), Times.Exactly(2));
        _perfRepo.Verify(r => r.AddAsync(It.IsAny<SignalPerformance>()), Times.Exactly(2));
    }
}
