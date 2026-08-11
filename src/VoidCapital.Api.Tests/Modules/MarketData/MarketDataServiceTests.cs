using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Modules.MarketData;

public class MarketDataServiceTests
{
    private readonly Mock<IMarketDataRepository> _repo = new();
    private readonly MemoryDistributedCache _cache = new(
        Options.Create(new MemoryDistributedCacheOptions()));

    private MarketDataService CreateService() => new(
        _repo.Object,
        _cache,
        NullLogger<MarketDataService>.Instance);

    [Fact]
    public async Task GetCurrentPrice_CacheMiss_FetchesFromRepoAndCaches()
    {
        _repo.Setup(r => r.GetLatestPriceAsync("RELIANCE")).ReturnsAsync(2500.50m);

        var price = await CreateService().GetCurrentPriceAsync("RELIANCE");

        price.Should().Be(2500.50m);
        _repo.Verify(r => r.GetLatestPriceAsync("RELIANCE"), Times.Once);

        // Second call hits the cache: repo not touched again.
        var again = await CreateService().GetCurrentPriceAsync("RELIANCE");
        again.Should().Be(2500.50m);
        _repo.Verify(r => r.GetLatestPriceAsync("RELIANCE"), Times.Once);
    }

    [Fact]
    public async Task GetCurrentPrice_NoData_ThrowsNotFound()
    {
        _repo.Setup(x => x.GetLatestPriceAsync("GHOST")).ReturnsAsync((decimal?)null);

        var act = async () => await CreateService().GetCurrentPriceAsync("GHOST");

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*GHOST*");
    }

    [Fact]
    public async Task GetCurrentPrice_CacheHit_DoesNotTouchRepo()
    {
        _repo.Setup(x => x.GetLatestPriceAsync("TCS")).ReturnsAsync(3500m);
        var service = CreateService();
        await service.GetCurrentPriceAsync("TCS");  // populate cache

        _repo.Invocations.Clear();
        var price = await service.GetCurrentPriceAsync("TCS");

        price.Should().Be(3500m);
        _repo.Verify(r => r.GetLatestPriceAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetCurrentPriceFresh_BypassesCache_ReadsRepo()
    {
        _repo.Setup(r => r.GetLatestPriceAsync("RELIANCE")).ReturnsAsync(2500.50m);
        var service = CreateService();

        // Populate the cache with a stale value via the cached path.
        await service.GetCurrentPriceAsync("RELIANCE");

        // Repo now has a newer quote; the fresh path must see it despite the
        // cache still holding the old one (D3: signal resolution correctness).
        _repo.Setup(r => r.GetLatestPriceAsync("RELIANCE")).ReturnsAsync(2600.00m);
        var price = await service.GetCurrentPriceFreshAsync("RELIANCE");

        price.Should().Be(2600.00m);
        _repo.Verify(r => r.GetLatestPriceAsync("RELIANCE"), Times.Exactly(2));
    }

    [Fact]
    public async Task GetCurrentPriceFresh_NoData_ThrowsNotFound()
    {
        _repo.Setup(x => x.GetLatestPriceAsync("GHOST")).ReturnsAsync((decimal?)null);

        var act = async () => await CreateService().GetCurrentPriceFreshAsync("GHOST");

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*GHOST*");
    }

    [Fact]
    public async Task GetPriceHistory_DelegatesToRepo()
    {
        var from = new DateOnly(2024, 1, 1);
        var to = new DateOnly(2024, 1, 31);
        var bars = new[]
        {
            new StockPrice("RELIANCE", from, 100m, 105m, 99m, 104m, 1000),
            new StockPrice("RELIANCE", new DateOnly(2024, 1, 2), 104m, 106m, 103m, 105m, 1200),
        };
        _repo.Setup(x => x.GetPriceHistoryAsync("RELIANCE", from, to))
            .ReturnsAsync(bars);

        var result = await CreateService().GetPriceHistoryAsync("RELIANCE", from, to);

        result.Should().BeEquivalentTo(bars);
        _repo.Verify(x => x.GetPriceHistoryAsync("RELIANCE", from, to), Times.Once);
    }
}