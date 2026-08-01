using FluentAssertions;
using Moq;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Services;

public class SignalPerformanceServiceTests
{
    private readonly Mock<ISignalPerformanceRepository> _perfRepo = new();
    private readonly Mock<IMarketDataService> _marketData = new();

    private SignalPerformanceService CreateService() => new(_perfRepo.Object, _marketData.Object);

    private static SignalPerformance MakePendingPerformance(decimal entry, decimal? target, decimal? stop, DateTime createdAt) => new()
    {
        Id = 1,
        EntryPrice = entry,
        TargetPrice = target,
        StopLoss = stop,
        Outcome = "PENDING",
        EvaluationDays = 5,
        CreatedAt = createdAt,
        Signal = new Signal { Symbol = "RELIANCE" }
    };

    private void GivenPrice(decimal price) =>
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(price);

    private void GivenPending(params SignalPerformance[] perfs) =>
        _perfRepo.Setup(r => r.GetPendingPerformancesAsync()).ReturnsAsync(perfs);

    [Fact]
    public async Task ResolvePendingSignals_WhenPriceHitsTarget_MarksHitTarget()
    {
        var perf = MakePendingPerformance(entry: 100m, target: 110m, stop: 90m, DateTime.UtcNow);
        GivenPending(perf);
        GivenPrice(112m);

        await CreateService().ResolvePendingSignalsAsync();

        perf.Outcome.Should().Be("HIT_TARGET");
        perf.ExitPrice.Should().Be(112m);
        perf.ActualReturn.Should().BeApproximately(0.12m, 0.001m);
        perf.ResolvedAt.Should().NotBeNull();
        _perfRepo.Verify(r => r.UpdateAsync(perf), Times.Once);
    }

    [Fact]
    public async Task ResolvePendingSignals_WhenPriceHitsStop_MarksHitStop()
    {
        var perf = MakePendingPerformance(entry: 100m, target: 110m, stop: 90m, DateTime.UtcNow);
        GivenPending(perf);
        GivenPrice(88m);

        await CreateService().ResolvePendingSignalsAsync();

        perf.Outcome.Should().Be("HIT_STOP");
        perf.ExitPrice.Should().Be(88m);
        perf.ActualReturn.Should().BeApproximately(-0.12m, 0.001m);
    }

    [Fact]
    public async Task ResolvePendingSignals_WhenPastEvaluationDays_MarksExpired()
    {
        var perf = MakePendingPerformance(
            entry: 100m, target: 110m, stop: 90m,
            createdAt: DateTime.UtcNow.AddDays(-10));
        GivenPending(perf);
        GivenPrice(105m);

        await CreateService().ResolvePendingSignalsAsync();

        perf.Outcome.Should().Be("EXPIRED");
        perf.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolvePendingSignals_WhenWithinHorizonAndPriceBetween_LeavesPending()
    {
        var perf = MakePendingPerformance(entry: 100m, target: 110m, stop: 90m, DateTime.UtcNow);
        GivenPending(perf);
        GivenPrice(105m);

        await CreateService().ResolvePendingSignalsAsync();

        perf.Outcome.Should().Be("PENDING");
        perf.ResolvedAt.Should().BeNull();
        _perfRepo.Verify(r => r.UpdateAsync(It.IsAny<SignalPerformance>()), Times.Never);
    }

    [Fact]
    public async Task ResolvePendingSignals_WhenSignalMissing_Skips()
    {
        var perf = MakePendingPerformance(entry: 100m, target: 110m, stop: 90m, DateTime.UtcNow);
        perf.Signal = null;
        GivenPending(perf);

        await CreateService().ResolvePendingSignalsAsync();

        _marketData.Verify(m => m.GetCurrentPriceAsync(It.IsAny<string>()), Times.Never);
        _perfRepo.Verify(r => r.UpdateAsync(It.IsAny<SignalPerformance>()), Times.Never);
    }
}
