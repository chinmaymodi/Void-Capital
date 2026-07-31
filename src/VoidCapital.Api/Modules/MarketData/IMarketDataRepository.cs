namespace VoidCapital.Api.Modules.MarketData;

public interface IMarketDataRepository
{
    Task<decimal?> GetLatestPriceAsync(string symbol);
    Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to);
}
