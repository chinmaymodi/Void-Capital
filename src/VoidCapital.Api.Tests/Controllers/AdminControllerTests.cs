using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
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

namespace VoidCapital.Api.Tests.Controllers;

public class AdminControllerTests
{
    private readonly Mock<ISignalRepository> _signalRepo = new();
    private readonly Mock<ISignalPerformanceRepository> _perfRepo = new();
    private readonly Mock<ISettingsRepository> _settingsRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IHoldingRepository> _holdingRepo = new();
    private readonly Mock<IPortfolioService> _portfolioService = new();
    private readonly Mock<ISignalIntegrationService> _signalIntegration = new();
    private readonly Mock<ISignalJobService> _signalJobService = new();

    private AdminController CreateController() => new(
        _signalRepo.Object,
        _perfRepo.Object,
        _settingsRepo.Object,
        _userRepo.Object,
        _holdingRepo.Object,
        _portfolioService.Object,
        _signalIntegration.Object,
        _signalJobService.Object);

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

        var result = await CreateController().GetSettings(2);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SettingsDto>>().Subject;
        envelope.Data!.UserId.Should().Be(2);
        envelope.Data.NegativeLimit.Should().Be(100000m);
        envelope.Data.Watchlist.Should().BeEquivalentTo("RELIANCE", "TCS");
    }

    [Fact]
    public async Task GetSettings_WhenMissing_ThrowsNotFound()
    {
        _settingsRepo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync((UserSettings?)null);

        var act = () => CreateController().GetSettings(99);

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

        var result = await CreateController().UpdateSettings(2, request);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SettingsDto>>().Subject;
        envelope.Data!.NegativeLimit.Should().Be(200000m);
        envelope.Data.InterestRate.Should().Be(0.001m);
        envelope.Data.Watchlist.Should().BeEquivalentTo("INFY");
        _settingsRepo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateGlobalSettings_AppliesToAllUsers()
    {
        var s2 = MakeSettings();
        var s3 = new UserSettings { Id = 2, UserId = 3, Watchlist = "[]" };
        _settingsRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[] { s2, s3 });

        var result = await CreateController().UpdateGlobalSettings(
            new GlobalSettingsRequest(MinConfidence: 0.55m, Watchlist: new[] { "HDFCBANK", "INFY" }));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<SettingsDto>>>().Subject;
        envelope.Data.Should().HaveCount(2);
        envelope.Data.Should().OnlyContain(d => d.MinConfidence == 0.55m);
        envelope.Data.Should().OnlyContain(d => d.Watchlist.SequenceEqual(new[] { "HDFCBANK", "INFY" }));
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

        var result = await CreateController().SquareOff(3);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SquareOffResultDto>>().Subject;
        envelope.Data!.PositionsSold.Should().Be(2);
        envelope.Data.Proceeds.Should().Be(48000m);
        envelope.Data.RemainingCash.Should().Be(49000m); // 1000 + 48000
    }

    [Fact]
    public async Task SquareOff_WhenUserMissing_ThrowsNotFound()
    {
        _userRepo.Setup(r => r.GetByIdAsync(99)).ReturnsAsync((User?)null);

        var act = () => CreateController().SquareOff(99);

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

        var result = await CreateController().SquareOff(3);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SquareOffResultDto>>().Subject;
        envelope.Data!.Proceeds.Should().Be(20000m);
        envelope.Data.RemainingCash.Should().Be(0m);
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

        var result = await CreateController().GetStatus();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<AdminStatusDto>>().Subject;
        envelope.Data!.PendingSignalCount.Should().Be(3);
        envelope.Data.Users.Should().HaveCount(2);
        envelope.Data.Users.First(u => u.UserId == 1).TotalReturn.Should().Be(10000m);
        envelope.Data.Users.First(u => u.UserId == 1).TotalReturnPercent.Should().Be(0.1m);
    }

    // ---------- Run signals (async job) ----------

    [Fact]
    public void RunSignals_StartsJobAndReturnsAcceptedWithRunningStatus()
    {
        var job = new SignalJob { JobId = 7, StartedAt = DateTime.UtcNow };
        _signalJobService.Setup(s => s.Start()).Returns(job);

        var result = CreateController().RunSignals();

        var accepted = result.Result.Should().BeOfType<AcceptedResult>().Subject;
        var envelope = accepted.Value.Should().BeOfType<ApiResponse<SignalJobDto>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data!.JobId.Should().Be(7);
        envelope.Data.Status.Should().Be("RUNNING");
        _signalJobService.Verify(s => s.Start(), Times.Once);
    }

    [Fact]
    public void GetRunSignalsStatus_ReturnsJobStatus()
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

        var result = CreateController().GetRunSignalsStatus(7);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SignalJobDto>>().Subject;
        envelope.Data!.Status.Should().Be("SUCCEEDED");
        envelope.Data.Message.Should().Contain("3 user(s)");
    }

    [Fact]
    public void GetRunSignalsStatus_UnknownJob_Returns404()
    {
        _signalJobService.Setup(s => s.Get(99)).Returns((SignalJob?)null);

        var act = () => CreateController().GetRunSignalsStatus(99);

        act.Should().Throw<NotFoundException>();
    }
}
