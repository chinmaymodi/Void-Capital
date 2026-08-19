using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

public class TradesControllerTests
{
    private readonly Mock<ITradeRepository> _tradeRepo = new();
    private TradesController CreateController() => new TradesController(_tradeRepo.Object).AsAdmin();

    private static Trade MakeTrade(int id = 1) => new()
    {
        Id = id,
        UserId = 1,
        Symbol = "RELIANCE",
        Type = "BUY",
        Quantity = 10,
        Price = 2850m,
        TotalValue = 28500m,
        Reason = "SMA crossover",
        Timestamp = new DateTime(2026, 8, 1, 10, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task GetTrades_ReturnsPagedEnvelope()
    {
        _tradeRepo.Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .ReturnsAsync((new[] { MakeTrade() }, 42));

        var result = await CreateController().GetTrades(1);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<PagedResult<TradeDto>>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data!.Items.Should().HaveCount(1);
        envelope.Data.Total.Should().Be(42);
    }

    [Fact]
    public async Task GetTrades_ForwardsFiltersToRepository()
    {
        TradeQuery? captured = null;
        _tradeRepo
            .Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .Callback<int, TradeQuery>((_, q) => captured = q)
            .ReturnsAsync((new[] { MakeTrade() }, 1));

        await CreateController().GetTrades(
            userId: 1,
            page: 2,
            pageSize: 50,
            symbol: "RELIANCE",
            type: "BUY",
            from: new DateOnly(2026, 7, 1),
            to: new DateOnly(2026, 8, 1));

        captured.Should().NotBeNull();
        captured!.Page.Should().Be(2);
        captured.PageSize.Should().Be(50);
        captured.Symbol.Should().Be("RELIANCE");
        captured.Type.Should().Be("BUY");
        captured.From.Should().Be(new DateOnly(2026, 7, 1));
        captured.To.Should().Be(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task GetTrades_ClampsInvalidPaging()
    {
        _tradeRepo.Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .ReturnsAsync((Array.Empty<Trade>(), 0));

        var result = await CreateController().GetTrades(userId: 1, page: 0, pageSize: 500);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<PagedResult<TradeDto>>>().Subject;
        // Clamped: page >= 1, pageSize in [1..100]
        envelope.Data!.Page.Should().Be(1);
        envelope.Data.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task GetTrades_ClampsPagingBeforeQuery()
    {
        // T2: the repository must never see an unbounded pageSize.
        TradeQuery? captured = null;
        _tradeRepo
            .Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .Callback<int, TradeQuery>((_, q) => captured = q)
            .ReturnsAsync((Array.Empty<Trade>(), 0));

        await CreateController().GetTrades(userId: 1, page: 0, pageSize: 500);

        captured!.Page.Should().Be(1);
        captured.PageSize.Should().Be(100);
    }

    [Fact]
    public async Task ExportTrades_ReturnsCsvWithHeaderAndRows()
    {
        _tradeRepo.Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .ReturnsAsync((new[] { MakeTrade() }, 1));

        var result = await CreateController().ExportTrades(1);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().StartWith("text/csv");
        file.FileDownloadName.Should().StartWith("trades_1_");

        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents!);
        csv.Should().StartWith("id,symbol,type,quantity,price,total_value,commission,reason,timestamp");
        csv.Should().Contain("1,RELIANCE,BUY,10,2850,28500,0,SMA crossover,");
    }

    [Fact]
    public async Task ExportTrades_EscapesCommasAndQuotesInCsv()
    {
        var trade = MakeTrade();
        trade.Reason = "quote, \"inside\" value";
        _tradeRepo.Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .ReturnsAsync((new[] { trade }, 1));

        var result = await CreateController().ExportTrades(1);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents!);
        csv.Should().Contain("\"quote, \"\"inside\"\" value\"");
    }

    [Theory]
    [InlineData("=SUM(A1:A9)")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    [InlineData("\t=1+1")]
    public async Task ExportTrades_NeutralizesExcelFormulaInjection(string reason)
    {
        // T3: cells starting with a formula/command character must be prefixed
        // with a single quote so Excel renders them as text, not executes them.
        var trade = MakeTrade();
        trade.Reason = reason;
        _tradeRepo.Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .ReturnsAsync((new[] { trade }, 1));

        var result = await CreateController().ExportTrades(1);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents!);
        csv.Should().Contain("'" + reason);
    }

    [Fact]
    public async Task ExportTrades_DoesNotPrefixNormalReasons()
    {
        var trade = MakeTrade();
        trade.Reason = "SMA crossover bullish";
        _tradeRepo.Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .ReturnsAsync((new[] { trade }, 1));

        var result = await CreateController().ExportTrades(1);

        var file = result.Should().BeOfType<FileContentResult>().Subject;
        var csv = System.Text.Encoding.UTF8.GetString(file.FileContents!);
        csv.Should().Contain("SMA crossover bullish");
        csv.Should().NotContain("'SMA crossover bullish");
    }

    [Fact]
    public async Task ExportTrades_UsesPageSizeMax_ToIncludeAllRows()
    {
        TradeQuery? captured = null;
        _tradeRepo
            .Setup(r => r.QueryAsync(1, It.IsAny<TradeQuery>()))
            .Callback<int, TradeQuery>((_, q) => captured = q)
            .ReturnsAsync((Array.Empty<Trade>(), 0));

        await CreateController().ExportTrades(1);

        captured!.Page.Should().Be(1);
        captured.PageSize.Should().Be(int.MaxValue);
    }
}
