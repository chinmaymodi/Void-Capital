namespace VoidCapital.Api.Modules.MarketData;

public interface IMarketDataService
{
    Task<decimal> GetCurrentPriceAsync(string symbol);

    /// <summary>
    /// Latest price straight from the DB, bypassing the Redis cache. Used by
    /// signal resolution (D3): the daily cycle writes fresh signals and then
    /// resolves them in the same run, so a 1-hour-old cached price could
    /// immediately hit target/stop against a stale quote.
    /// </summary>
    Task<decimal> GetCurrentPriceFreshAsync(string symbol);

    Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to);
}
