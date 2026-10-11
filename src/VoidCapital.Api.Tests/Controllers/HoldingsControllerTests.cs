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

public class HoldingsControllerTests
{
    private readonly Mock<IPortfolioService> _service = new();

    private HoldingsController CreateController() => new(_service.Object);

    private static HoldingDto SampleHolding() => new(
        Id: 1, Symbol: "RELIANCE", Shares: 10, AvgBuyPrice: 100m,
        CurrentPrice: 150m, UnrealizedPnl: 500m, PercentOfPortfolio: 0.25m);

    private static Trade SampleTrade() => new()
    {
        Id = 1,
        UserId = 7,
        Symbol = "RELIANCE",
        Type = "BUY",
        Quantity = 10,
        Price = 500m,
        TotalValue = 5000m,
        Commission = 6.55m,
        Reason = "Manual trade",
        Timestamp = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task GetHoldings_Admin_ReturnsHoldings()
    {
        _service.Setup(s => s.GetHoldingsAsync(7)).ReturnsAsync(new[] { SampleHolding() });

        var result = await CreateController().AsAdmin().GetHoldings(7);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<HoldingDto>>>().Subject;
        envelope.Data.Should().HaveCount(1);
        _service.Verify(s => s.GetHoldingsAsync(7), Times.Once);
    }

    [Fact]
    public async Task GetHoldings_OtherUser_Forbids()
    {
        var controller = CreateController().AsUser(3);
        var result = await controller.GetHoldings(4);

        result.Result.Should().BeOfType<ForbidResult>();
        _service.Verify(s => s.GetHoldingsAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Buy_Admin_ExecutesAndReturnsTradeDto()
    {
        _service.Setup(s => s.ExecuteBuyAsync(7, "RELIANCE", 10)).ReturnsAsync(SampleTrade());

        var result = await CreateController().AsAdmin().Buy(7, new TradeRequest("RELIANCE", 10));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<TradeDto>>().Subject;
        envelope.Data.Symbol.Should().Be("RELIANCE");
        envelope.Data.Shares.Should().Be(10);
        envelope.Data.Price.Should().Be(500m);
        envelope.Data.Total.Should().Be(5000m);
        envelope.Data.Commission.Should().Be(6.55m);
        _service.Verify(s => s.ExecuteBuyAsync(7, "RELIANCE", 10), Times.Once);
    }

    [Fact]
    public async Task Buy_OtherUser_Forbids()
    {
        var controller = CreateController().AsUser(3);
        var result = await controller.Buy(4, new TradeRequest("RELIANCE", 10));

        result.Result.Should().BeOfType<ForbidResult>();
        _service.Verify(s => s.ExecuteBuyAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task Sell_Admin_ExecutesAndReturnsTradeDto()
    {
        _service.Setup(s => s.ExecuteSellAsync(7, "RELIANCE", 3)).ReturnsAsync(SampleTrade());

        var result = await CreateController().AsAdmin().Sell(7, new TradeRequest("RELIANCE", 3));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<TradeDto>>().Subject;
        envelope.Data.Symbol.Should().Be("RELIANCE");
        _service.Verify(s => s.ExecuteSellAsync(7, "RELIANCE", 3), Times.Once);
    }

    [Fact]
    public async Task Sell_OtherUser_Forbids()
    {
        var controller = CreateController().AsUser(3);
        var result = await controller.Sell(4, new TradeRequest("RELIANCE", 3));

        result.Result.Should().BeOfType<ForbidResult>();
        _service.Verify(s => s.ExecuteSellAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }
}