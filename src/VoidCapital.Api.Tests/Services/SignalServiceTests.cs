using FluentAssertions;
using Moq;
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

    private SignalService CreateService() => new(
        _signalRepo.Object,
        _settingsRepo.Object,
        _portfolioService.Object);

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
}
