namespace VoidCapital.Api.Modules.MarketData;

public interface IMarketDataRepository
{
    Task<decimal?> GetLatestPriceAsync(string symbol);
    Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to);

    /// <summary>
    /// Latest settle price of one option contract from market_data.fo_options
    /// (per-strike bhavcopy rows). Null when the contract has no data.
    /// </summary>
    Task<decimal?> GetOptionPriceAsync(string symbol, DateOnly expiry, decimal strike, string optType);

    /// <summary>
    /// Newest bar timestamp in market_data.stocks_intraday_1m (live collector
    /// freshness check, D19). Null when the table is empty.
    /// </summary>
    Task<DateTime?> GetLatestIntradayTimestampAsync();
}
