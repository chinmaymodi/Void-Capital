namespace VoidCapital.Api.Modules.MarketData;

public interface IMarketDataService
{
    Task<decimal> GetCurrentPriceAsync(string symbol, CancellationToken ct = default);

    /// <summary>
    /// Latest price straight from the DB, bypassing the Redis cache. Used by
    /// signal resolution (D3): the daily cycle writes fresh signals and then
    /// resolves them in the same run, so a 1-hour-old cached price could
    /// immediately hit target/stop against a stale quote.
    /// </summary>
    Task<decimal> GetCurrentPriceFreshAsync(string symbol, CancellationToken ct = default);

    Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to, CancellationToken ct = default);

    /// <summary>
    /// Latest settle of one option contract (D16). Straight from the DB --
    /// fo_options is daily bhavcopy data refreshed once per day, so the
    /// Redis cache adds nothing.
    /// </summary>
    Task<decimal> GetOptionPriceAsync(string symbol, DateOnly expiry, decimal strike, string optType, CancellationToken ct = default);
}
