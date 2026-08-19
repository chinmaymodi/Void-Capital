using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

/// <summary>
/// M1: null/empty/whitespace symbols must be rejected with a 400
/// (ValidationException, mapped by ExceptionMiddleware) instead of a 500 from
/// ToUpperInvariant() on null or a pointless service round-trip.
/// </summary>
public class MarketDataControllerTests
{
    private readonly Mock<IMarketDataService> _marketData = new();

    private MarketDataController CreateController() => new(_marketData.Object);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetPrice_BlankSymbol_ThrowsValidationException(string? symbol)
    {
        var act = () => CreateController().GetPrice(symbol!, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _marketData.Verify(m => m.GetCurrentPriceAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetPrice_ValidSymbol_UppercasesAndForwards()
    {
        _marketData.Setup(m => m.GetCurrentPriceAsync("RELIANCE")).ReturnsAsync(2850m);

        var result = await CreateController().GetPrice(" reliance ", CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<decimal>>().Subject;
        envelope.Data.Should().Be(2850m);
        _marketData.Verify(m => m.GetCurrentPriceAsync("RELIANCE"), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetHistory_BlankSymbol_ThrowsValidationException(string? symbol)
    {
        var act = () => CreateController().GetHistory(symbol!, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
        _marketData.Verify(m => m.GetPriceHistoryAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<DateOnly>()), Times.Never);
    }

    [Fact]
    public async Task GetHistory_ValidSymbol_UppercasesAndForwards()
    {
        var history = new[] { new StockPrice("RELIANCE", new DateOnly(2026, 8, 1), 2850m, 2900m, 2800m, 2880m, 1000) };
        _marketData
            .Setup(m => m.GetPriceHistoryAsync("RELIANCE", It.IsAny<DateOnly>(), It.IsAny<DateOnly>()))
            .ReturnsAsync(history);

        var result = await CreateController().GetHistory("reliance", null, null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<IEnumerable<StockPrice>>>().Subject;
        envelope.Data.Should().HaveCount(1);
        _marketData.Verify(m => m.GetPriceHistoryAsync("RELIANCE", It.IsAny<DateOnly>(), It.IsAny<DateOnly>()), Times.Once);
    }
}