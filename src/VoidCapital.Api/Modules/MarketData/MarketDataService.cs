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

    public async Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default)
    {
        var cacheKey = $"stock:{symbol}:price";
        var cached = await _cache.GetStringAsync(cacheKey, ct);
        if (cached != null && decimal.TryParse(cached, out var cachedPrice))
        {
            _logger.LogDebug("Cache hit for {Key}", cacheKey);
            return cachedPrice;
        }

        var price = await _repo.GetLatestPriceAsync(symbol)
            ?? throw new NotFoundException($"No market data for symbol '{symbol}'");

        await _cache.SetStringAsync(cacheKey, price.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl }, ct);

        _logger.LogDebug("Cache miss for {Key}; fetched from DB", cacheKey);
        return price;
    }

    public async Task<decimal> GetCurrentPriceFreshAsync(string symbol, CancellationToken ct = default)
    {
        // D3: no cache read, no cache write -- the caller (signal resolution)
        // needs the DB's latest quote, not a quote that may predate the
        // refresh that just ran in the same daily cycle.
        return await _repo.GetLatestPriceAsync(symbol)
            ?? throw new NotFoundException($"No market data for symbol '{symbol}'");
    }

    public Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default) =>
        _repo.GetPriceHistoryAsync(symbol, from, to);

    public async Task<decimal> GetOptionPriceAsync(string symbol, DateOnly expiry, decimal strike, string optType, CancellationToken ct = default)
    {
        // D16: options fills price at the reconstructed settle (fo_options),
        // not the stock quote. Read fresh -- bhavcopy settles land once per
        // day and the daily cycle may have just written them.
        return await _repo.GetOptionPriceAsync(symbol, expiry, strike, optType)
            ?? throw new NotFoundException(
                $"No option data for {symbol} {optType} {expiry} strike {strike}");
    }
}
