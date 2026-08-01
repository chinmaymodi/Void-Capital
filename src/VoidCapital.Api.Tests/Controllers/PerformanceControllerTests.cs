using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

public class PerformanceControllerTests
{
    private readonly Mock<ISignalRepository> _signalRepo = new();
    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<IPortfolioService> _portfolioService = new();

    private PerformanceController CreateController() =>
        new(_signalRepo.Object, _userRepo.Object, _portfolioService.Object);

    [Fact]
    public async Task GetModels_ReturnsPerModelStats()
    {
        var stats = new[]
        {
            new ModelPerformanceDto("sma", 10, 8, 6, 0.75m, 0.05m, 0.12m, -0.03m),
            new ModelPerformanceDto("rsi", 8, 5, 2, 0.40m, 0.01m, 0.06m, -0.02m)
        };
        _signalRepo.Setup(r => r.GetModelPerformanceAsync()).ReturnsAsync(stats);

        var result = await CreateController().GetModels();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<ModelPerformanceDto>>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSignals_ReturnsPagedResolvedSignals()
    {
        var items = new[]
        {
            new ResolvedSignalDto(1, new DateOnly(2026, 8, 1), "RELIANCE", "BUY", "sma",
                2860m, 3000m, 3010m, "HIT_TARGET", 0.05m, DateTime.UtcNow, 5)
        };
        _signalRepo
            .Setup(r => r.GetResolvedAsync(It.IsAny<PerformanceQuery>()))
            .ReturnsAsync((items, 1));

        var result = await CreateController().GetSignals(userId: 2, model: "sma", page: 1, pageSize: 20);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        envelope.Success.Should().BeTrue();
        _signalRepo.Verify(r => r.GetResolvedAsync(
            It.Is<PerformanceQuery>(q => q.UserId == 2 && q.Model == "sma")), Times.Once);
    }

    [Fact]
    public async Task GetSignals_ClampsPageSize()
    {
        _signalRepo
            .Setup(r => r.GetResolvedAsync(It.IsAny<PerformanceQuery>()))
            .ReturnsAsync((Array.Empty<ResolvedSignalDto>(), 0));

        var result = await CreateController().GetSignals(pageSize: 500);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        _signalRepo.Verify(r => r.GetResolvedAsync(
            It.Is<PerformanceQuery>(q => q.PageSize == 100)), Times.Once);
    }

    [Fact]
    public async Task GetCompare_ReturnsPortfoliosAndGaps()
    {
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 1, Name = "Trader One", StartingBudget = 100000m, CurrentCash = 90000m },
            new User { Id = 2, Name = "System", StartingBudget = 100000m, CurrentCash = 110000m }
        });
        _portfolioService
            .Setup(p => p.GetPortfolioStateAsync(1))
            .ReturnsAsync(new PortfolioStateDto(90000m, 20000m, 110000m));
        _portfolioService
            .Setup(p => p.GetPortfolioStateAsync(2))
            .ReturnsAsync(new PortfolioStateDto(110000m, 0m, 110000m));

        var result = await CreateController().GetCompare();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<PortfolioComparisonDto>>().Subject;
        envelope.Data!.Portfolios.Should().HaveCount(2);
        envelope.Data.Portfolios.First(p => p.UserId == 1).TotalReturn.Should().Be(10000m);
        envelope.Data.Portfolios.First(p => p.UserId == 1).TotalReturnPercent.Should().Be(0.1m);

        // Tied totals produce a gap of zero; a leader is still named.
        envelope.Data.Gaps.Should().ContainSingle();
        envelope.Data.Gaps.Single().GapRupees.Should().Be(0m);
    }

    [Fact]
    public async Task GetCompare_ComputesPairwiseGap()
    {
        _userRepo.Setup(r => r.GetAllAsync()).ReturnsAsync(new[]
        {
            new User { Id = 1, Name = "Trader One", StartingBudget = 100000m, CurrentCash = 90000m },
            new User { Id = 2, Name = "System", StartingBudget = 100000m, CurrentCash = 110000m }
        });
        _portfolioService
            .Setup(p => p.GetPortfolioStateAsync(1))
            .ReturnsAsync(new PortfolioStateDto(90000m, 0m, 90000m));
        _portfolioService
            .Setup(p => p.GetPortfolioStateAsync(2))
            .ReturnsAsync(new PortfolioStateDto(110000m, 0m, 110000m));

        var result = await CreateController().GetCompare();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<PortfolioComparisonDto>>().Subject;
        var gap = envelope.Data!.Gaps.Should().ContainSingle().Subject;
        gap.Leader.Should().Be("System");
        gap.Trailer.Should().Be("Trader One");
        gap.GapRupees.Should().Be(20000m);
        gap.GapPercent.Should().BeApproximately(0.2222m, 0.001m);
    }
}
