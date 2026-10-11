using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

public class PortfolioControllerTests
{
    private readonly Mock<IPortfolioService> _service = new();

    private PortfolioController CreateController() => new(_service.Object);

    private static PortfolioStateDto SampleState() => new(50000m, 50000m, 100000m);

    [Fact]
    public async Task GetState_Admin_ReturnsState()
    {
        _service.Setup(s => s.GetPortfolioStateAsync(7)).ReturnsAsync(SampleState());

        var result = await CreateController().AsAdmin().GetState(7);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<PortfolioStateDto>>().Subject;
        envelope.Data.TotalValue.Should().Be(100000m);
        _service.Verify(s => s.GetPortfolioStateAsync(7), Times.Once);
    }

    [Fact]
    public async Task GetState_OwnUser_ReturnsState()
    {
        _service.Setup(s => s.GetPortfolioStateAsync(3)).ReturnsAsync(SampleState());

        var controller = CreateController().AsUser(3);
        var result = await controller.GetState(3);

        result.Result.Should().BeOfType<OkObjectResult>();
        _service.Verify(s => s.GetPortfolioStateAsync(3), Times.Once);
    }

    [Fact]
    public async Task GetState_OtherUser_Forbids()
    {
        var controller = CreateController().AsUser(3);
        var result = await controller.GetState(4);

        result.Result.Should().BeOfType<ForbidResult>();
        _service.Verify(s => s.GetPortfolioStateAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetHistory_Admin_ReturnsHistory()
    {
        var history = new[]
        {
            new PnlSnapshot
            {
                Id = 1, UserId = 7, Date = new DateOnly(2026, 9, 1),
                PortfolioValue = 100000m, CashValue = 50000m, HoldingsValue = 50000m
            }
        };
        _service.Setup(s => s.GetPnlHistoryAsync(7)).ReturnsAsync(history);

        var result = await CreateController().AsAdmin().GetHistory(7);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<PnlSnapshot>>>().Subject;
        envelope.Data.Should().HaveCount(1);
        _service.Verify(s => s.GetPnlHistoryAsync(7), Times.Once);
    }

    [Fact]
    public async Task GetHistory_OtherUser_Forbids()
    {
        var controller = CreateController().AsUser(3);
        var result = await controller.GetHistory(4);

        result.Result.Should().BeOfType<ForbidResult>();
        _service.Verify(s => s.GetPnlHistoryAsync(It.IsAny<int>()), Times.Never);
    }
}