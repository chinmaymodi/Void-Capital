namespace VoidCapital.Api.Modules.MarketData;

public interface IMarketDataService
{
    Task<decimal> GetCurrentPriceAsync(string symbol);
    Task<IEnumerable<StockPrice>> GetPriceHistoryAsync(string symbol, DateOnly from, DateOnly to);
}
