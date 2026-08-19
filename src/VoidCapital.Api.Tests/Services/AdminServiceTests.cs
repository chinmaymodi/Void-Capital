using FluentAssertions;
using Moq;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Services;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Services;

/// <summary>
/// A3: AdminService holds the admin orchestration logic (signal ingestion,
/// settings management, square-off, status). These tests were ported from the
/// old AdminControllerTests; the controller itself is now a thin mapping layer
/// covered by AdminControllerTests.
/// </summary>
public class AdminServiceTests
{
    private readonly Mock<ISignalRepository> _signalRepo = new();
    private readonly Mock<ISignalPerformanceRepository> _perfRepo = new();
    private readonly Mock<ISettingsRepository> _settingsRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IHoldingRepository> _holdingRepo = new();
    private readonly Mock<IPortfolioService> _portfolioService = new();
    private readonly Mock<ISignalJobService> _signalJobService = new();
    private readonly Mock<IDailyCycleRunner> _dailyCycleRunner = new();

    private AdminService CreateService() => new(
        _signalRepo.Object,
        _perfRepo.Object,
        _settingsRepo.Object,
        _userRepo.Object,
        _holdingRepo.Object,
        _portfolioService.Object,
        _signalJobService.Object,
        _dailyCycleRunner.Object);

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

    // ---------- Ingest signals ----------

    [Fact]
    public async Task IngestSignals_CreatesSignalWithUserIdAndQuantity()
    {
        Signal? captured = null;
        _signalRepo
            .Setup(r => r.AddAsync(It.IsAny<Signal>()))
            .Callback<Signal>(s => captured = s)
            .ReturnsAsync((Signal s) => s);

        // Snapshot the IST date around the call: the service stamps the IST
        // trading date (UtcNow + 5.5h) internally, so asserting against a
        // single re-evaluated UTC date flakes when the test straddles the
        // IST midnight (18:30 UTC).
        var before = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        var result = await CreateService().IngestSignalsAsync(new[] { MakeRequest() });
        var after = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));

        captured.Should().NotBeNull();
        captured!.UserId.Should().Be(1);
        captured.Symbol.Should().Be("RELIANCE");
        captured.SuggestedQuantity.Should().Be(10);
        captured.Status.Should().Be(SignalStatus.PENDING);
        captured.Date.Should().BeOnOrAfter(before);
        captured.Date.Should().BeOnOrBefore(after);
        result.Should().ContainSingle();
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

        await CreateService().IngestSignalsAsync(new[] { MakeRequest() });

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
        var act = () => CreateService().IngestSignalsAsync(new[] { MakeRequest(userId: null) });

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

        var result = await CreateService().IngestSignalsAsync(new[]
        {
            MakeRequest(),
            MakeRequest() with { Symbol = "TCS", UserId = 2 }
        });

        result.Should().HaveCount(2);
        _signalRepo.Verify(r => r.AddAsync(It.IsAny<Signal>()), Times.Exactly(2));
        _perfRepo.Verify(r => r.AddAsync(It.IsAny<SignalPerformance>()), Times.Exactly(2));
    }

    // ---------- Settings ----------

    private static UserSettings MakeSettings() => new()
    {
        Id = 1,
        UserId = 2,
        AutoExecute = true,
        MinConfidence = 0.5m,
        NegativeLimit = 100000m,
        InterestRate = 0.0005m,
        Watchlist = "[\"RELIANCE\",\"TCS\"]"
    };

    [Fact]
    public async Task GetSettings_ReturnsSettingsDto()
    {
        _settingsRepo.Setup(r => r.GetByUserIdAsync(2)).ReturnsAsync(MakeSettings());

        var dto = await CreateService().GetSettingsAsync(2);

        dto.UserId.Should().Be(2);
        dto.NegativeLimit.Should().Be(100000m);
        dto.Watchlist.Should().BeEquivalentTo("RELIANCE", "TCS");
    }

    [Fact]
    public async Task GetSettings_WhenMissing_ThrowsNotFound()
    {
        _settingsRepo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync((UserSettings?)null);

        var act = () => CreateService().GetSettingsAsync(99);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateSettings_PersistsAndReturnsDto()
    {
        var existing = MakeSettings();
        _settingsRepo.Setup(r => r.GetByUserIdAsync(2)).ReturnsAsync(existing);

        var request = new UpdateSettingsRequest(
            AutoExecute: false,
            MinConfidence: 0.6m,
            NegativeLimit: 200000m,
            InterestRate: 0.001m,
            Watchlist: new[] { "INFY" });

        var dto = await CreateService().UpdateSettingsAsync(2, request);

        dto.NegativeLimit.Should().Be(200000m);
        dto.InterestRate.Should().Be(0.001m);
        dto.Watchlist.Should().BeEquivalentTo("INFY");
        _settingsRepo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateGlobalSettings_AppliesToAllUsers()
    {
        var s2 = MakeSettings();
        var s3 = new UserSettings { Id = 2, UserId = 3, Watchlist = "[]" };
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { s2, s3 });

        var result = await CreateService().UpdateGlobalSettingsAsync(
            new GlobalSettingsRequest(0.55m, 0m, 0m, new[] { "HDFCBANK", "INFY" }));

        result.Should().HaveCount(2);
        result.Should().OnlyContain(d => d.MinConfidence == 0.55m);
        result.Should().OnlyContain(d => d.Watchlist.SequenceEqual(new[] { "HDFCBANK", "INFY" }));
        _settingsRepo.Verify(r => r.UpdateAsync(It.IsAny<UserSettings>()), Times.Exactly(2));
    }

    // ---------- Square-off ----------

    [Fact]
    public async Task SquareOff_SellsAllHoldingsAndReturnsProceeds()
    {
        // First read is the pre-liquidation balance; the sell loop credits
        // proceeds, so the post-liquidation read reflects the higher cash.
        _userRepo
            .SetupSequence(r => r.GetByIdAsync(3))
            .ReturnsAsync(new User { Id = 3, CurrentCash = 1000m })
            .ReturnsAsync(new User { Id = 3, CurrentCash = 49000m });
        _holdingRepo.Setup(r => r.GetByUserIdAsync(3)).ReturnsAsync(new[]
        {
            new Holding { Id = 1, UserId = 3, Symbol = "RELIANCE", Quantity = 10 },
            new Holding { Id = 2, UserId = 3, Symbol = "TCS", Quantity = 5 }
        });
        _portfolioService
            .Setup(p => p.ExecuteSellAsync(3, "RELIANCE", 10))
            .ReturnsAsync(new Trade { Symbol = "RELIANCE", Quantity = 10, TotalValue = 29000m });
        _portfolioService
            .Setup(p => p.ExecuteSellAsync(3, "TCS", 5))
            .ReturnsAsync(new Trade { Symbol = "TCS", Quantity = 5, TotalValue = 19000m });

        var result = await CreateService().SquareOffAsync(3);

        result.PositionsSold.Should().Be(2);
        result.Proceeds.Should().Be(48000m);
        result.RemainingCash.Should().Be(49000m); // 1000 + 48000
    }

    [Fact]
    public async Task SquareOff_WhenUserMissing_ThrowsNotFound()
    {
        _userRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((User?)null);

        var act = () => CreateService().SquareOffAsync(99);

        await act.Should().ThrowAsync<NotFoundException>();
        _holdingRepo.Verify(r => r.GetByUserIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SquareOff_WhenCashStillNegative_FloorsAtZero()
    {
        _userRepo.Setup(r => r.GetByIdAsync(3)).ReturnsAsync(new User { Id = 3, CurrentCash = -50000m });
        _holdingRepo.Setup(r => r.GetByUserIdAsync(3)).ReturnsAsync(new[]
        {
            new Holding { Id = 1, UserId = 3, Symbol = "RELIANCE", Quantity = 10 }
        });
        _portfolioService
            .Setup(p => p.ExecuteSellAsync(3, "RELIANCE", 10))
            .ReturnsAsync(new Trade { Symbol = "RELIANCE", Quantity = 10, TotalValue = 20000m });

        var result = await CreateService().SquareOffAsync(3);

        result.Proceeds.Should().Be(20000m);
        result.RemainingCash.Should().Be(0m);
        _userRepo.Verify(r => r.UpdateCashAsync(3, 0m), Times.Once);
    }

    // ---------- Status ----------

    [Fact]
    public async Task GetStatus_ReturnsPendingCountAndBalances()
    {
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 1, Name = "Trader One", StartingBudget = 100000m, CurrentCash = 90000m },
            new User { Id = 2, Name = "System", StartingBudget = 100000m, CurrentCash = 110000m }
        });
        _signalRepo.Setup(r => r.GetStatusCountsAsync()).ReturnsAsync(new Dictionary<SignalStatus, int>
        {
            [SignalStatus.PENDING] = 3,
            [SignalStatus.EXECUTED] = 7
        });
        _portfolioService
            .Setup(p => p.GetPortfolioStateAsync(1))
            .ReturnsAsync(new PortfolioStateDto(90000m, 20000m, 110000m));
        _portfolioService
            .Setup(p => p.GetPortfolioStateAsync(2))
            .ReturnsAsync(new PortfolioStateDto(110000m, 0m, 110000m));

        var status = await CreateService().GetStatusAsync();

        status.PendingSignalCount.Should().Be(3);
        status.Users.Should().HaveCount(2);
        status.Users.First(u => u.UserId == 1).TotalReturn.Should().Be(10000m);
        status.Users.First(u => u.UserId == 1).TotalReturnPercent.Should().Be(0.1m);
    }

    // ---------- Jobs ----------

    [Fact]
    public void GetSignalJob_WhenMissing_ThrowsNotFound()
    {
        _signalJobService.Setup(s => s.Get(99)).Returns((SignalJob?)null);

        var act = () => CreateService().GetSignalJob(99);

        act.Should().Throw<NotFoundException>();
    }

    [Fact]
    public void GetSignalJob_ReturnsJobDto()
    {
        var job = new SignalJob
        {
            JobId = 7,
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            FinishedAt = DateTime.UtcNow,
            Status = SignalJobStatus.SUCCEEDED,
            Message = "Signal generation complete: 3 user(s), 0 failures"
        };
        _signalJobService.Setup(s => s.Get(7)).Returns(job);

        var dto = CreateService().GetSignalJob(7);

        dto.JobId.Should().Be(7);
        dto.Status.Should().Be("SUCCEEDED");
        dto.Message.Should().Contain("3 user(s)");
    }
}