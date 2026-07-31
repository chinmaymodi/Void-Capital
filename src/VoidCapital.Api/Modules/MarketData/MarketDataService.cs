using Microsoft.Extensions.Caching.Distributed;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Modules.MarketData;

/// <summary>
/// Cache-Aside for market data: Redis first, DB on miss, store with TTL.
/// Redis is a cache, never the source of truth.
/// </summary>
public class MarketDataService : IMarketDataService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    private readonly IMarketDataRepository _repo;
    private readonly IDistributedCache _cache;
    private readonly ILogger<MarketDataService> _logger;

    public MarketDataService(
        IMarketDataRepository repo,
        IDistributedCache cache,
        ILogger<MarketDataService> logger)
    {
        _repo = repo;
        _cache = cache;
        _logger = logger;
    }

    public async Task<decimal> GetCurrentPriceAsync(string symbol)
    {
        var cacheKey = $"stock:{symbol}:price";
        var cached = await _cache.GetStringAsync(cacheKey);
        if (cached != null && decimal.TryParse(cached, out var cachedPrice))
        {
            _logger.LogDebug("Cache hit for {Key}", cacheKey);
            return cachedPrice;
        }

        var price = await _repo.GetLatestPriceAsync(symbol)
            ?? throw new NotFoundException($"No market data for symbol '{symbol}'");

        await _cache.SetStringAsync(cacheKey, price.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl });

        _logger.LogDebug("Cache miss for {Key}; fetched from DB", cacheKey);
        return price;
    }

    public Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to) =>
        _repo.GetPriceHistoryAsync(symbol, from, to);
}
