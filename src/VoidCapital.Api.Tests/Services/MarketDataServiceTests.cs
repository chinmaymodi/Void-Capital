using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Services;

public class MarketDataServiceTests
{
    private readonly Mock<IMarketDataRepository> _repo = new();
    private readonly MemoryDistributedCache _cache;

    public MarketDataServiceTests()
    {
        _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    }

    private MarketDataService CreateService() =>
        new(_repo.Object, _cache, NullLogger<MarketDataService>.Instance);

    [Fact]
    public async Task GetCurrentPrice_CacheMiss_FetchesFromRepo_AndCaches()
    {
        _repo.Setup(r => r.GetLatestPriceAsync("RELIANCE")).ReturnsAsync(2850.00m);

        var service = CreateService();
        var price = await service.GetCurrentPriceAsync("RELIANCE");

        price.Should().Be(2850.00m);
        _repo.Verify(r => r.GetLatestPriceAsync("RELIANCE"), Times.Once);

        // Second call should be served from cache without touching the repo.
        var cachedPrice = await service.GetCurrentPriceAsync("RELIANCE");
        cachedPrice.Should().Be(2850.00m);
        _repo.Verify(r => r.GetLatestPriceAsync("RELIANCE"), Times.Once);
    }

    [Fact]
    public async Task GetCurrentPrice_CacheHit_SkipsRepository()
    {
        _repo.Setup(r => r.GetLatestPriceAsync("TCS")).ReturnsAsync(3800.00m);
        var service = CreateService();

        // Warm the cache.
        await service.GetCurrentPriceAsync("TCS");

        // New service instance sharing the same cache: repo must NOT be called.
        var second = CreateService();
        var price = await second.GetCurrentPriceAsync("TCS");

        price.Should().Be(3800.00m);
        _repo.Verify(r => r.GetLatestPriceAsync("TCS"), Times.Once);
    }

    [Fact]
    public async Task GetCurrentPrice_WhenNoData_ThrowsNotFound()
    {
        _repo.Setup(r => r.GetLatestPriceAsync("NOPE")).ReturnsAsync((decimal?)null);

        var service = CreateService();
        var act = () => service.GetCurrentPriceAsync("NOPE");

        await act.Should().ThrowAsync<NotFoundException>()
            .WithMessage("*NOPE*");
    }

    [Fact]
    public async Task GetPriceHistory_DelegatesToRepository()
    {
        var from = new DateOnly(2026, 1, 1);
        var to = new DateOnly(2026, 1, 31);
        _repo.Setup(r => r.GetPriceHistoryAsync("INFY", from, to))
            .ReturnsAsync(new[]
            {
                new StockPrice("INFY", from, 100m, 110m, 99m, 105m, 1000),
                new StockPrice("INFY", to, 106m, 112m, 104m, 108m, 1200)
            });

        var service = CreateService();
        var history = await service.GetPriceHistoryAsync("INFY", from, to);

        history.Should().HaveCount(2);
        _repo.Verify(r => r.GetPriceHistoryAsync("INFY", from, to), Times.Once);
    }
}
