using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Services;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Tests.Services;

public class DailyCycleServiceTests
{
    private readonly Mock<ISignalIntegrationService> _signalIntegration = new();
    private readonly Mock<ISignalService> _signalService = new();
    private readonly Mock<ISignalRepository> _signalRepo = new();
    // SignalPerformanceService has no parameterless ctor; Moq needs the real
    // ctor args to build the proxy. ResolvePendingSignalsAsync is not virtual,
    // so the real method runs against the stubbed repo (empty pending list).
    private readonly Mock<ISignalPerformanceRepository> _performanceRepo = new();
    private readonly Mock<IMarketDataService> _marketData = new();
    private readonly Mock<SignalPerformanceService> _performanceService;
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IPortfolioService> _portfolioService = new();
    private readonly Mock<ISettingsRepository> _settingsRepo = new();
    private readonly Mock<IHoldingRepository> _holdingRepo = new();
    private readonly Mock<ICycleRunRepository> _cycleRunRepo = new();

    public DailyCycleServiceTests()
    {
        _performanceService = new(_performanceRepo.Object, _marketData.Object);
    }

    private DailyCycleRunner CreateRunner() => new(
        _signalIntegration.Object,
        _signalService.Object,
        _signalRepo.Object,
        _performanceService.Object,
        _userRepo.Object,
        _portfolioService.Object,
        _settingsRepo.Object,
        _holdingRepo.Object,
        _cycleRunRepo.Object,
        NullLogger<DailyCycleRunner>.Instance);

    private void SetupEmptyUsers()
    {
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<User>());
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(Array.Empty<UserSettings>());
        _performanceRepo
            .Setup(r => r.GetPendingPerformancesAsync())
            .ReturnsAsync(Array.Empty<SignalPerformance>());
        _signalIntegration
            .Setup(s => s.RunForAllUsersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SignalRunSummary(0, 0, []));
    }

    private static UserSettings MakeSettings(int userId, bool autoExecute = false, decimal minConfidence = 0.5m,
        decimal negativeLimit = 0m, decimal interestRate = 0m) => new()
    {
        Id = userId,
        UserId = userId,
        AutoExecute = autoExecute,
        MinConfidence = minConfidence,
        NegativeLimit = negativeLimit,
        InterestRate = interestRate,
        Watchlist = "[]"
    };

    private static Signal MakeSignal(int id, int userId, string action = "BUY", decimal confidence = 0.7m) => new()
    {
        Id = id,
        UserId = userId,
        Date = DateOnly.FromDateTime(DateTime.UtcNow),
        Symbol = "RELIANCE",
        Action = action,
        Confidence = confidence,
        ModelName = "Ensemble",
        Status = SignalStatus.PENDING,
        SuggestedQuantity = 10
    };

    [Fact]
    public async Task RunAsync_RecordsSucceededRunWithCounts()
    {
        SetupEmptyUsers();
        var run = new CycleRun { Id = 1 };
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync(run);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SUCCEEDED", result.Status);
        _cycleRunRepo.Verify(r => r.AddAsync(It.IsAny<CycleRun>()), Times.Once);
        _cycleRunRepo.Verify(r => r.UpdateAsync(It.Is<CycleRun>(c => c.Status == "SUCCEEDED")), Times.Once);
    }

    [Fact]
    public async Task RunAsync_AutoExecutesSignals_ForAutoExecuteUsersOnly()
    {
        SetupEmptyUsers();
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            MakeSettings(2, autoExecute: true, minConfidence: 0.5m),
            MakeSettings(3, autoExecute: false)
        });
        _signalRepo.Setup(r => r.GetTodaySignalsAsync(2)).ReturnsAsync(new[]
        {
            MakeSignal(1, 2, confidence: 0.7m),  // above threshold -> eligible
            MakeSignal(2, 2, confidence: 0.3m)   // below threshold -> skipped
        });
        _signalService
            .Setup(s => s.BatchApproveAsync(new[] { 1 }))
            .ReturnsAsync(new[] { SignalBatchResult.Ok(1) });
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SUCCEEDED", result.Status);
        Assert.Equal(1, result.SignalsExecuted);
        // User 3 has auto-execute off: its signals must never be fetched
        _signalRepo.Verify(r => r.GetTodaySignalsAsync(3), Times.Never);
    }

    [Fact]
    public async Task RunAsync_ChargesInterestOnNegativeCash()
    {
        SetupEmptyUsers();
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 3, Name = "Reckless", CurrentCash = -100000m }
        });
        _settingsRepo.Setup(r => r.GetByUserIdAsync(3))
            .ReturnsAsync(MakeSettings(3, negativeLimit: 100000m, interestRate: 0.0005m));
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        await CreateRunner().RunAsync();

        // interest = -100000 * 0.0005 / 365, cash becomes more negative
        _userRepo.Verify(r => r.UpdateCashAsync(3, It.Is<decimal>(c => c < -100000m)), Times.Once);
    }

    [Fact]
    public async Task RunAsync_MarginCall_SquaresOffWhenBelowLimit()
    {
        SetupEmptyUsers();
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 3, Name = "Reckless", CurrentCash = -150000m }
        });
        _settingsRepo.Setup(r => r.GetByUserIdAsync(3))
            .ReturnsAsync(MakeSettings(3, negativeLimit: 100000m, interestRate: 0.0005m));
        _holdingRepo.Setup(r => r.GetByUserIdAsync(3)).ReturnsAsync(new[]
        {
            new Holding { Id = 1, UserId = 3, Symbol = "RELIANCE", Quantity = 10 }
        });
        _portfolioService
            .Setup(p => p.ExecuteSellAsync(3, "RELIANCE", 10))
            .ReturnsAsync(new Trade { Symbol = "RELIANCE", Quantity = 10, TotalValue = 20000m });
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SUCCEEDED", result.Status);
        _portfolioService.Verify(p => p.ExecuteSellAsync(3, "RELIANCE", 10), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WhenSignalGenerationFails_MarksRunFailedAndKeepsGoing()
    {
        SetupEmptyUsers();
        _signalIntegration
            .Setup(s => s.RunForAllUsersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("python exploded"));
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("FAILED", result.Status);
        Assert.Contains("python exploded", result.Error);
    }
}
