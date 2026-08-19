using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
    private readonly Mock<IPythonBridge> _pythonBridge = new();
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
    private readonly Mock<IProcessRunner> _processRunner = new();
    private readonly Mock<ICycleLock> _cycleLock = new();
    private readonly Mock<ICycleLease> _cycleLease = new();

    public DailyCycleServiceTests()
    {
        _performanceService = new(_performanceRepo.Object, _marketData.Object);
    }

    private DailyCycleRunner CreateRunner() => new(
        _pythonBridge.Object,
        _signalIntegration.Object,
        _signalService.Object,
        _signalRepo.Object,
        _performanceService.Object,
        _userRepo.Object,
        _portfolioService.Object,
        _settingsRepo.Object,
        _holdingRepo.Object,
        _cycleRunRepo.Object,
        _processRunner.Object,
        _cycleLock.Object,
        Options.Create(new PythonSettings { NotificationScriptPath = "" }),
        NullLogger<DailyCycleRunner>.Instance);

    /// <summary>
    /// Builds the scheduler with a mocked scope factory so the catch-up path
    /// resolves the mocked cycle-run repo and runner from a scope.
    /// </summary>
    private DailyCycleService CreateService()
    {
        var scopeProvider = new Mock<IServiceProvider>();
        scopeProvider.Setup(p => p.GetService(typeof(ICycleRunRepository))).Returns(_cycleRunRepo.Object);
        scopeProvider.Setup(p => p.GetService(typeof(IDailyCycleRunner))).Returns(CreateRunner());
        var scope = new Mock<IServiceScope>();
        scope.Setup(s => s.ServiceProvider).Returns(scopeProvider.Object);
        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);
        var rootProvider = new Mock<IServiceProvider>();
        rootProvider.Setup(p => p.GetService(typeof(IServiceScopeFactory))).Returns(scopeFactory.Object);
        return new DailyCycleService(rootProvider.Object, NullLogger<DailyCycleService>.Instance);
    }

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
        _pythonBridge
            .Setup(b => b.RunDataRefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PythonRunResult(true, "", ""));
        // DS1: the lock is free by default so the cycle actually runs.
        _cycleLock
            .Setup(l => l.TryAcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_cycleLease.Object);
    }

    private static UserSettings MakeSettings(int userId, bool autoExecute = false, decimal minConfidence = 0.5m,
        decimal negativeLimit = 0m, decimal interestRate = 0m, bool isHalted = false) => new()
    {
        Id = userId,
        UserId = userId,
        AutoExecute = autoExecute,
        IsHalted = isHalted,
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
        // First re-read (post-interest) still below the limit -> margin call
        // fires; second re-read (post-liquidation) recovered -> no halt.
        _userRepo.SetupSequence(r => r.GetByIdAsync(3))
            .ReturnsAsync(new User { Id = 3, CurrentCash = -150000m })
            .ReturnsAsync(new User { Id = 3, CurrentCash = -50000m });
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SUCCEEDED", result.Status);
        _portfolioService.Verify(p => p.ExecuteSellAsync(3, "RELIANCE", 10), Times.Once);
        // F12: liquidation recovered, agent must NOT be halted.
        _settingsRepo.Verify(r => r.UpdateAsync(It.Is<UserSettings>(s => s.IsHalted)), Times.Never);
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

    [Fact]
    public async Task RunAsync_RefreshesFeatures_BeforeSignalGeneration()
    {
        SetupEmptyUsers();
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        await CreateRunner().RunAsync();

        _pythonBridge.Verify(b => b.RunDataRefreshAsync(It.IsAny<CancellationToken>()), Times.Once);
        _signalIntegration.Verify(s => s.RunForAllUsersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_RefreshFailure_ContinuesCycleOnYesterdayFeatures()
    {
        SetupEmptyUsers();
        _pythonBridge
            .Setup(b => b.RunDataRefreshAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PythonRunResult(false, "", "iv computation timed out"));
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        // Approved D1 behavior: log-and-continue, the cycle still succeeds.
        Assert.Equal("SUCCEEDED", result.Status);
        _signalIntegration.Verify(s => s.RunForAllUsersAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_RefreshThrows_ContinuesCycle()
    {
        SetupEmptyUsers();
        _pythonBridge
            .Setup(b => b.RunDataRefreshAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("python crashed"));
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SUCCEEDED", result.Status);
    }

    [Fact]
    public async Task RunAsync_MarginCallSellFailure_ContinuesWithRemainingHoldings()
    {
        // D1: one un-sellable holding must not strand the rest of the user's
        // liquidation or abort the cycle.
        SetupEmptyUsers();
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 3, Name = "Reckless", CurrentCash = -150000m }
        });
        _settingsRepo.Setup(r => r.GetByUserIdAsync(3))
            .ReturnsAsync(MakeSettings(3, negativeLimit: 100000m, interestRate: 0.0005m));
        _holdingRepo.Setup(r => r.GetByUserIdAsync(3)).ReturnsAsync(new[]
        {
            new Holding { Id = 1, UserId = 3, Symbol = "RELIANCE", Quantity = 10 },
            new Holding { Id = 2, UserId = 3, Symbol = "HDFCBANK", Quantity = 5 }
        });
        _portfolioService
            .Setup(p => p.ExecuteSellAsync(3, "RELIANCE", 10))
            .ThrowsAsync(new InvalidOperationException("quote missing"));
        _portfolioService
            .Setup(p => p.ExecuteSellAsync(3, "HDFCBANK", 5))
            .ReturnsAsync(new Trade { Symbol = "HDFCBANK", Quantity = 5, TotalValue = 30000m });
        // First re-read (post-interest) still below the limit -> margin call
        // fires; second re-read (post-liquidation) recovered -> no halt.
        _userRepo.SetupSequence(r => r.GetByIdAsync(3))
            .ReturnsAsync(new User { Id = 3, CurrentCash = -150000m })
            .ReturnsAsync(new User { Id = 3, CurrentCash = -40000m });
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        // Both sells were attempted; the failing one did not abort the second.
        _portfolioService.Verify(p => p.ExecuteSellAsync(3, "RELIANCE", 10), Times.Once);
        _portfolioService.Verify(p => p.ExecuteSellAsync(3, "HDFCBANK", 5), Times.Once);
        Assert.Equal("SUCCEEDED", result.Status);
    }

    [Fact]
    public async Task RunAsync_AutoExecuteFailure_ContinuesWithOtherUsers()
    {
        // D1: one user's execution failure must not skip the remaining users.
        SetupEmptyUsers();
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            MakeSettings(2, autoExecute: true, minConfidence: 0.5m),
            MakeSettings(3, autoExecute: true, minConfidence: 0.5m)
        });
        _signalRepo.Setup(r => r.GetTodaySignalsAsync(2)).ReturnsAsync(new[]
        {
            MakeSignal(1, 2, confidence: 0.7m)
        });
        _signalRepo.Setup(r => r.GetTodaySignalsAsync(3)).ReturnsAsync(new[]
        {
            MakeSignal(2, 3, confidence: 0.8m)
        });
        _signalService
            .Setup(s => s.BatchApproveAsync(new[] { 1 }))
            .ThrowsAsync(new InvalidOperationException("execution exploded"));
        _signalService
            .Setup(s => s.BatchApproveAsync(new[] { 2 }))
            .ReturnsAsync(new[] { SignalBatchResult.Ok(2) });
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        _signalService.Verify(s => s.BatchApproveAsync(new[] { 1 }), Times.Once);
        _signalService.Verify(s => s.BatchApproveAsync(new[] { 2 }), Times.Once);
        Assert.Equal("SUCCEEDED", result.Status);
        // User 2's failure is logged, not counted as executed.
        Assert.Equal(1, result.SignalsExecuted);
    }

    [Fact]
    public async Task RunAsync_SnapshotFailure_ContinuesWithOtherUsers()
    {
        // D1: a PnL snapshot failure for one user must not skip the rest.
        SetupEmptyUsers();
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 1, Name = "A", CurrentCash = 1000m },
            new User { Id = 2, Name = "B", CurrentCash = 2000m }
        });
        _portfolioService
            .Setup(p => p.RecordDailySnapshotAsync(1))
            .ThrowsAsync(new InvalidOperationException("snapshot exploded"));
        _portfolioService
            .Setup(p => p.RecordDailySnapshotAsync(2))
            .Returns(Task.CompletedTask);
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        _portfolioService.Verify(p => p.RecordDailySnapshotAsync(1), Times.Once);
        _portfolioService.Verify(p => p.RecordDailySnapshotAsync(2), Times.Once);
        Assert.Equal("SUCCEEDED", result.Status);
    }

    [Fact]
    public async Task RunAsync_WhenLockHeld_SkipsWithoutRecordingRun()
    {
        // DS1: another instance holds the advisory lock -> skip, no run row,
        // no pipeline work.
        SetupEmptyUsers();
        _cycleLock
            .Setup(l => l.TryAcquireAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((ICycleLease?)null);
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SKIPPED", result.Status);
        _cycleRunRepo.Verify(r => r.AddAsync(It.IsAny<CycleRun>()), Times.Never);
        _signalIntegration.Verify(s => s.RunForAllUsersAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_MarginCall_DetectsBreachCausedByInterest()
    {
        // D2: pre-interest cash (-99999.99) is above the -100000 limit; the
        // interest charge pushes it below. The margin call must fire today.
        SetupEmptyUsers();
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 3, Name = "Reckless", CurrentCash = -99999.99m }
        });
        _settingsRepo.Setup(r => r.GetByUserIdAsync(3))
            .ReturnsAsync(MakeSettings(3, negativeLimit: 100000m, interestRate: 0.0005m));
        // Post-interest re-read: -99999.99 - (-99999.99 * 0.0005 / 365) is
        // just under -100000, so the re-read crosses the limit. The second
        // call is the F12 post-liquidation re-read: proceeds recovered.
        _userRepo.SetupSequence(r => r.GetByIdAsync(3))
            .ReturnsAsync(new User { Id = 3, CurrentCash = -100000.13m })
            .ReturnsAsync(new User { Id = 3, CurrentCash = -50000m });
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
    public async Task RunAsync_HaltedUser_NoAutoExecute()
    {
        // F12: a halted agent is terminal - auto-execute is skipped even when
        // AutoExecute is true, so its signals are never even fetched.
        SetupEmptyUsers();
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            MakeSettings(2, autoExecute: true, minConfidence: 0.5m, isHalted: true),
            MakeSettings(3, autoExecute: true, minConfidence: 0.5m)
        });
        _signalRepo.Setup(r => r.GetTodaySignalsAsync(3)).ReturnsAsync(new[]
        {
            MakeSignal(1, 3, confidence: 0.7m)
        });
        _signalService
            .Setup(s => s.BatchApproveAsync(new[] { 1 }))
            .ReturnsAsync(new[] { SignalBatchResult.Ok(1) });
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SUCCEEDED", result.Status);
        Assert.Equal(1, result.SignalsExecuted);
        // User 2 is halted: its signals must never be fetched or executed.
        _signalRepo.Verify(r => r.GetTodaySignalsAsync(2), Times.Never);
    }

    [Fact]
    public async Task RunAsync_HaltedUser_NoInterestNoMarginCall()
    {
        // F12: halted agents freeze - no interest accrual on the deficit and
        // no further margin calls (the liquidation already failed once).
        SetupEmptyUsers();
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 3, Name = "Reckless", CurrentCash = -150000m }
        });
        _settingsRepo.Setup(r => r.GetByUserIdAsync(3))
            .ReturnsAsync(MakeSettings(3, negativeLimit: 100000m, interestRate: 0.0005m, isHalted: true));
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        await CreateRunner().RunAsync();

        _userRepo.Verify(r => r.UpdateCashAsync(3, It.IsAny<decimal>()), Times.Never);
        _holdingRepo.Verify(r => r.GetByUserIdAsync(3), Times.Never);
        _portfolioService.Verify(p => p.ExecuteSellAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_FailedLiquidation_HaltsAgent()
    {
        // F12 terminal rule: the margin call squares off, but the deficit
        // survives the liquidation (cash still below the negative limit).
        // The agent is dead - halt it so it stops trading and stops accruing
        // interest on the permanent deficit. Admin revive only.
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
        // Post-liquidation re-read: proceeds did NOT cover the deficit.
        _userRepo.Setup(r => r.GetByIdAsync(3))
            .ReturnsAsync(new User { Id = 3, CurrentCash = -120000m });
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SUCCEEDED", result.Status);
        _portfolioService.Verify(p => p.ExecuteSellAsync(3, "RELIANCE", 10), Times.Once);
        // The settings row is persisted with the terminal flag set.
        _settingsRepo.Verify(r => r.UpdateAsync(It.Is<UserSettings>(s =>
            s.UserId == 3 && s.IsHalted)), Times.Once);
    }

    [Fact]
    public async Task RunAsync_FailedLiquidation_NoHoldings_HaltsAgent()
    {
        // F13: holdings < deficit. The agent has nothing to sell, so the
        // square-off cannot recover the deficit. The post-square-off recheck
        // (F12 terminal rule) must halt it immediately - otherwise the cycle
        // repeats the same losing square-off daily.
        SetupEmptyUsers();
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 3, Name = "Reckless", CurrentCash = -150000m }
        });
        _settingsRepo.Setup(r => r.GetByUserIdAsync(3))
            .ReturnsAsync(MakeSettings(3, negativeLimit: 100000m, interestRate: 0.0005m));
        // No holdings: the liquidation sells nothing.
        _holdingRepo.Setup(r => r.GetByUserIdAsync(3)).ReturnsAsync(Array.Empty<Holding>());
        // Post-liquidation re-read: cash unchanged, still below the limit.
        _userRepo.Setup(r => r.GetByIdAsync(3))
            .ReturnsAsync(new User { Id = 3, CurrentCash = -150000m });
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var result = await CreateRunner().RunAsync();

        Assert.Equal("SUCCEEDED", result.Status);
        _portfolioService.Verify(p => p.ExecuteSellAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        _settingsRepo.Verify(r => r.UpdateAsync(It.Is<UserSettings>(s =>
            s.UserId == 3 && s.IsHalted)), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_StaleRunningRun_AbortsAndRunsCatchUp()
    {
        var startedAt = DateTime.UtcNow.AddHours(-5);
        var stale = new CycleRun { Id = 1, Status = "RUNNING", StartedAt = startedAt };
        _cycleRunRepo.Setup(r => r.GetRecentAsync(1)).ReturnsAsync(new[] { stale });
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);
        SetupEmptyUsers();
        _cycleRunRepo.Setup(r => r.AddAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        // The catch-up path runs on the background ExecuteAsync task; wait
        // until the fresh run is recorded (the last step of the chain) before
        // tearing down, so the verifies below are not racing the task.
        await WaitUntilAsync(() =>
            _cycleRunRepo.Invocations.Any(i => i.Method.Name == nameof(ICycleRunRepository.AddAsync)));

        await service.StopAsync(CancellationToken.None);

        // The stale run is aborted with FinishedAt left null so NeedsCatchUp
        // still sees the slot as missed and catch-up fires.
        _cycleRunRepo.Verify(r => r.UpdateAsync(It.Is<CycleRun>(c =>
            c.Status == "FAILED" &&
            c.FinishedAt == null &&
            c.Error!.Contains("stuck in RUNNING"))), Times.Once);
        // Catch-up fired: a fresh run was recorded after the abort.
        _cycleRunRepo.Verify(r => r.AddAsync(It.IsAny<CycleRun>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_FinishedRun_SuppressesCatchUp()
    {
        var finishedAt = DateTime.UtcNow.AddHours(-1);
        var lastRun = new CycleRun
        {
            Id = 1,
            Status = "SUCCEEDED",
            StartedAt = finishedAt.AddMinutes(-5),
            FinishedAt = finishedAt
        };
        _cycleRunRepo.Setup(r => r.GetRecentAsync(1)).ReturnsAsync(new[] { lastRun });
        _cycleRunRepo.Setup(r => r.UpdateAsync(It.IsAny<CycleRun>())).ReturnsAsync((CycleRun r) => r);

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        // Wait until the scheduler actually read the last run, otherwise the
        // Never verifies below pass trivially even if ExecuteAsync never ran.
        await WaitUntilAsync(() =>
            _cycleRunRepo.Invocations.Any(i => i.Method.Name == nameof(ICycleRunRepository.GetRecentAsync)));

        await service.StopAsync(CancellationToken.None);

        // Slot already served: no abort, no catch-up run.
        _cycleRunRepo.Verify(r => r.UpdateAsync(It.IsAny<CycleRun>()), Times.Never);
        _cycleRunRepo.Verify(r => r.AddAsync(It.IsAny<CycleRun>()), Times.Never);
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds or the timeout elapses.
    /// Used to synchronize with work running on the background ExecuteAsync
    /// task, which is otherwise racy to verify against.
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Background service did not reach the expected state in time.");
            await Task.Delay(10);
        }
    }
}
