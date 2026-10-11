using FluentAssertions;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using Xunit;

namespace VoidCapital.Api.Tests.Models;

/// <summary>
/// Construct-and-read tests for plain models/DTOs. These exist so coverlet
/// counts every auto-property/positional-record line; the values themselves
/// are trivial, the point is the getters/setters are exercised.
/// </summary>
public class ModelConstructionTests
{
    [Fact]
    public void WatchlistItem_RoundTripsAllProperties()
    {
        var item = new WatchlistItem
        {
            Id = 1,
            UserId = 2,
            Symbol = "RELIANCE",
            AddedDate = new DateOnly(2026, 9, 1)
        };

        item.Id.Should().Be(1);
        item.UserId.Should().Be(2);
        item.Symbol.Should().Be("RELIANCE");
        item.AddedDate.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public void HoldingDto_RoundTripsAllProperties()
    {
        var dto = new HoldingDto(
            Id: 1, Symbol: "RELIANCE", Shares: 10, AvgBuyPrice: 100m,
            CurrentPrice: 150m, UnrealizedPnl: 500m, PercentOfPortfolio: 0.25m,
            InstrumentType: "CE", Expiry: new DateOnly(2026, 10, 29), Strike: 2800m);

        dto.Id.Should().Be(1);
        dto.Symbol.Should().Be("RELIANCE");
        dto.Shares.Should().Be(10);
        dto.AvgBuyPrice.Should().Be(100m);
        dto.CurrentPrice.Should().Be(150m);
        dto.UnrealizedPnl.Should().Be(500m);
        dto.PercentOfPortfolio.Should().Be(0.25m);
        dto.InstrumentType.Should().Be("CE");
        dto.Expiry.Should().Be(new DateOnly(2026, 10, 29));
        dto.Strike.Should().Be(2800m);
    }

    [Fact]
    public void HoldingDto_DefaultsToEquity()
    {
        var dto = new HoldingDto(1, "RELIANCE", 10, 100m, 150m, 500m, 0.25m);

        dto.InstrumentType.Should().Be("EQ");
        dto.Expiry.Should().BeNull();
        dto.Strike.Should().BeNull();
    }

    [Fact]
    public void StockPrice_RoundTripsAllProperties()
    {
        var price = new StockPrice("RELIANCE", new DateOnly(2026, 9, 1),
            2850m, 2900m, 2800m, 2880m, 1000);

        price.Symbol.Should().Be("RELIANCE");
        price.Date.Should().Be(new DateOnly(2026, 9, 1));
        price.Open.Should().Be(2850m);
        price.High.Should().Be(2900m);
        price.Low.Should().Be(2800m);
        price.Close.Should().Be(2880m);
        price.Volume.Should().Be(1000);
    }
}