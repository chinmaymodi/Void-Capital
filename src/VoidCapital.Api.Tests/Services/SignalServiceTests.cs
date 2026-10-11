using FluentAssertions;
using Moq;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Modules.Signals.Services;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Services;

public class SignalServiceTests
{
    private readonly Mock<ISignalRepository> _signalRepo = new();
    private readonly Mock<ISettingsRepository> _settingsRepo = new();
    private readonly Mock<IPortfolioService> _portfolioService = new();
    private readonly Mock<IMarketDataService> _marketData = new();

    private SignalService CreateService() => new(
        _signalRepo.Object,
        _settingsRepo.Object,
        _portfolioService.Object,
        _marketData.Object);

    private static Signal MakePendingSignal(string action = "BUY", int? quantity = 10, int userId = 1) => new()
    {
        Id = 1,
        UserId = userId,
        Symbol = "RELIANCE",
        Action = action,
        SuggestedQuantity = quantity,
        Status = SignalStatus.PENDING,
        Date = DateOnly.FromDateTime(DateTime.UtcNow)
    };

    private void GivenSettings(bool autoExecute, int userId = 1) =>
        _settingsRepo
            .Setup(r => r.GetByUserIdAsync(userId))
            .ReturnsAsync(new UserSettings { UserId = userId, AutoExecute = autoExecute });

    private void GivenPrice(decimal price = 500m) =>
        _marketData.Setup(m => m.GetCurrentPriceAsync(It.IsAny<string>()))
            .ReturnsAsync(price);

    private void GivenSignal(Signal signal) =>
        _signalRepo.Setup(r => r.GetByIdAsync(signal.Id)).ReturnsAsync(signal);

    // ---------- Approve: manual mode ----------

    [Fact]
    public async Task ApproveSignal_WhenPendingAndAutoExecuteOff_MarksApproved()
    {
        var signal = MakePendingSignal();
        GivenSignal(signal);
        GivenSettings(autoExecute: false);

        var dto = await CreateService().ApproveSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.APPROVED);
        dto.Status.Should().Be("APPROVED");
        _signalRepo.Verify(r => r.UpdateAsync(It.Is<Signal>(s => s.Status == SignalStatus.APPROVED)), Times.Once);
    }

    [Fact]
    public async Task ApproveSignal_WhenNotPending_Throws()
    {
        var signal = MakePendingSignal();
        signal.Status = SignalStatus.APPROVED;
        GivenSignal(signal);
        GivenSettings(autoExecute: false);

        var service = CreateService();
        var act = () => service.ApproveSignalAsync(signal.Id);

        await act.Should().ThrowAsync<ValidationException>();
        _signalRepo.Verify(r => r.UpdateAsync(It.IsAny<Signal>()), Times.Never);
    }

    [Fact]
    public async Task ApproveSignal_WhenMissing_ThrowsNotFound()
    {
        GivenSettings(autoExecute: false);

        var service = CreateService();
        var act = () => service.ApproveSignalAsync(99);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---------- Approve: auto-execute mode ----------

    [Fact]
    public async Task ApproveSignal_WhenAutoExecuteOn_CallsExecuteBuyAndMarksExecuted()
    {
        var signal = MakePendingSignal(action: "BUY", quantity: 10);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice();

        await CreateService().ApproveSignalAsync(signal.Id);

        _portfolioService.Verify(p => p.ExecuteBuyAsync(signal.UserId, "RELIANCE", 10), Times.Once);
        _portfolioService.Verify(p => p.ExecuteSellAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
        signal.Status.Should().Be(SignalStatus.EXECUTED);
    }

    [Fact]
    public async Task ApproveSignal_WhenAutoExecuteOnSell_CallsExecuteSellAndMarksExecuted()
    {
        var signal = MakePendingSignal(action: "SELL", quantity: 5);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice();

        await CreateService().ApproveSignalAsync(signal.Id);

        _portfolioService.Verify(p => p.ExecuteSellAsync(signal.UserId, "RELIANCE", 5), Times.Once);
        signal.Status.Should().Be(SignalStatus.EXECUTED);
    }

    [Fact]
    public async Task ApproveSignal_WhenAutoExecuteOnAndExecutionFails_MarksFailedWithReason()
    {
        var signal = MakePendingSignal(action: "BUY", quantity: 10);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice();
        _portfolioService
            .Setup(p => p.ExecuteBuyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()))
            .ThrowsAsync(new InsufficientFundsException("Insufficient funds for purchase"));

        await CreateService().ApproveSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().Contain("Insufficient funds");
    }

    [Fact]
    public async Task ApproveSignal_WhenAutoExecuteOnAndNoQuantity_MarksFailed()
    {
        var signal = MakePendingSignal(action: "BUY", quantity: null);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice();

        await CreateService().ApproveSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().NotBeNullOrWhiteSpace();
        _portfolioService.Verify(p => p.ExecuteBuyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    // ---------- Reject ----------

    [Fact]
    public async Task RejectSignal_WhenPending_MarksRejected()
    {
        var signal = MakePendingSignal();
        GivenSignal(signal);
        GivenSettings(autoExecute: false);

        var dto = await CreateService().RejectSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.REJECTED);
        dto.Status.Should().Be("REJECTED");
        _portfolioService.Verify(p => p.ExecuteBuyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    // ---------- Batch ----------

    [Fact]
    public async Task BatchApprove_ProcessesEachSignal()
    {
        var signal1 = MakePendingSignal();
        signal1.Id = 1;
        var signal2 = MakePendingSignal();
        signal2.Id = 2;
        GivenSignal(signal1);
        GivenSignal(signal2);
        GivenSettings(autoExecute: false);

        var results = await CreateService().BatchApproveAsync([1, 2]);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Success);
        _signalRepo.Verify(r => r.UpdateAsync(It.IsAny<Signal>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BatchApprove_WhenOneFails_ReturnsPerItemResults()
    {
        var signal1 = MakePendingSignal();
        signal1.Id = 1;
        var processed = MakePendingSignal();
        processed.Id = 2;
        processed.Status = SignalStatus.APPROVED;
        GivenSignal(signal1);
        GivenSignal(processed);
        GivenSettings(autoExecute: false);

        var results = await CreateService().BatchApproveAsync([1, 2]);

        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Id == 1 && r.Success);
        results.Should().Contain(r => r.Id == 2 && !r.Success && r.Error != null);
    }

    // ---------- Get today ----------

    [Fact]
    public async Task GetTodaySignals_ReturnsSignalsForUser()
    {
        var signal = MakePendingSignal();
        _signalRepo.Setup(r => r.GetTodaySignalsAsync(1))
            .ReturnsAsync(new[] { signal });

        var signals = await CreateService().GetTodaySignalsAsync(1);

        signals.Should().ContainSingle();
        signals.First().Symbol.Should().Be("RELIANCE");
        signals.First().Status.Should().Be("PENDING");
    }

    // ---------- ExecuteApproved ----------

    [Fact]
    public async Task ExecuteApprovedSignal_WhenApproved_CallsExecuteBuyAndMarksExecuted()
    {
        var signal = MakePendingSignal(action: "BUY", quantity: 10);
        signal.Status = SignalStatus.APPROVED;
        GivenSignal(signal);
        GivenPrice();

        await CreateService().ExecuteApprovedSignalAsync(signal.Id);

        _portfolioService.Verify(p => p.ExecuteBuyAsync(signal.UserId, "RELIANCE", 10), Times.Once);
        signal.Status.Should().Be(SignalStatus.EXECUTED);
    }

    [Fact]
    public async Task ExecuteApprovedSignal_WhenNotApproved_Throws()
    {
        var signal = MakePendingSignal();
        GivenSignal(signal);
        GivenPrice();

        var service = CreateService();
        var act = () => service.ExecuteApprovedSignalAsync(signal.Id);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task ExecuteApprovedSignal_WhenMissing_ThrowsNotFound()
    {
        var service = CreateService();
        var act = () => service.ExecuteApprovedSignalAsync(99);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    // ---------- Batch reject ----------

    [Fact]
    public async Task BatchReject_ProcessesEachSignal()
    {
        var signal1 = MakePendingSignal();
        signal1.Id = 1;
        var signal2 = MakePendingSignal();
        signal2.Id = 2;
        GivenSignal(signal1);
        GivenSignal(signal2);

        var results = await CreateService().BatchRejectAsync([1, 2]);

        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.Success);
        _signalRepo.Verify(r => r.UpdateAsync(It.IsAny<Signal>()), Times.Exactly(2));
    }

    [Fact]
    public async Task BatchReject_WhenOneFails_ReturnsPerItemResults()
    {
        var signal1 = MakePendingSignal();
        signal1.Id = 1;
        var processed = MakePendingSignal();
        processed.Id = 2;
        processed.Status = SignalStatus.REJECTED;
        GivenSignal(signal1);
        GivenSignal(processed);

        var results = await CreateService().BatchRejectAsync([1, 2]);

        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Id == 1 && r.Success);
        results.Should().Contain(r => r.Id == 2 && !r.Success && r.Error != null);
    }

    // ---------- TryExecute: price guards ----------

    [Fact]
    public async Task ApproveSignal_WhenPriceZeroOrNegative_MarksFailed()
    {
        var signal = MakePendingSignal(action: "BUY", quantity: 10);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice(0m);

        await CreateService().ApproveSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().Contain("Price not found");
        _portfolioService.Verify(p => p.ExecuteBuyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ApproveSignal_WhenPriceDeviationExceedsBand_MarksFailed()
    {
        var signal = MakePendingSignal(action: "BUY", quantity: 10);
        signal.Performance = new SignalPerformance
        {
            EntryPrice = 500m,
            Outcome = "PENDING",
            EvaluationDays = 5,
            CreatedAt = DateTime.UtcNow
        };
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice(600m); // 20% above entry -> outside the 10% sanity band

        await CreateService().ApproveSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().Contain("Price deviation");
        _portfolioService.Verify(p => p.ExecuteBuyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    // ---------- TryExecute: options paths ----------

    private static Signal MakePendingOptionsSignal(string action, decimal? premium = null) => new()
    {
        Id = 1,
        UserId = 1,
        Symbol = "RELIANCE",
        InstrumentType = "CE",
        Expiry = new DateOnly(2026, 10, 29),
        Strike = 2800m,
        Action = action,
        SuggestedQuantity = 75,
        Status = SignalStatus.PENDING,
        Date = DateOnly.FromDateTime(DateTime.UtcNow),
        Performance = premium is null ? null : new SignalPerformance
        {
            EntryPrice = premium.Value,
            Outcome = "PENDING",
            EvaluationDays = 5,
            CreatedAt = DateTime.UtcNow
        }
    };

    [Fact]
    public async Task ApproveSignal_OptionsBuy_CallsExecuteOptionsBuy()
    {
        var signal = MakePendingOptionsSignal("BUY", premium: 45m);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice(45m); // market price == premium so the F11 band check passes

        await CreateService().ApproveSignalAsync(signal.Id);

        _portfolioService.Verify(p => p.ExecuteOptionsBuyAsync(
            1, "RELIANCE", "CE", new DateOnly(2026, 10, 29), 2800m, 75, 45m), Times.Once);
        signal.Status.Should().Be(SignalStatus.EXECUTED);
    }

    [Fact]
    public async Task ApproveSignal_OptionsBuy_MissingPremium_MarksFailed()
    {
        var signal = MakePendingOptionsSignal("BUY", premium: null);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice(45m);

        await CreateService().ApproveSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().Contain("no entry premium");
        _portfolioService.Verify(p => p.ExecuteOptionsBuyAsync(
            It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<DateOnly>(), It.IsAny<decimal>(), It.IsAny<int>(), It.IsAny<decimal>()), Times.Never);
    }

    [Fact]
    public async Task ApproveSignal_OptionsSell_CallsExecuteOptionsSell()
    {
        var signal = MakePendingOptionsSignal("SELL", premium: 45m);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice(45m);

        await CreateService().ApproveSignalAsync(signal.Id);

        _portfolioService.Verify(p => p.ExecuteOptionsSellAsync(
            1, "RELIANCE", "CE", new DateOnly(2026, 10, 29), 2800m, 75, It.IsAny<string>()), Times.Once);
        signal.Status.Should().Be(SignalStatus.EXECUTED);
    }

    [Fact]
    public async Task ApproveSignal_OptionsMissingExpiryOrStrike_MarksFailed()
    {
        var signal = MakePendingOptionsSignal("BUY", premium: 45m);
        signal.Expiry = null;
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice(45m);

        await CreateService().ApproveSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().Contain("missing expiry or strike");
    }

    // ---------- TryExecute: unknown action ----------

    [Fact]
    public async Task ApproveSignal_UnknownAction_MarksFailed()
    {
        var signal = MakePendingSignal(action: "HOLD", quantity: 10);
        GivenSignal(signal);
        GivenSettings(autoExecute: true);
        GivenPrice();

        await CreateService().ApproveSignalAsync(signal.Id);

        signal.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().Contain("Unknown signal action");
        _portfolioService.Verify(p => p.ExecuteBuyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}
