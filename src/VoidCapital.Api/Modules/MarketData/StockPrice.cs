namespace VoidCapital.Api.Modules.MarketData;

/// <summary>One EOD bar from market_data.stocks.</summary>
public record StockPrice(
    string Symbol,
    DateOnly Date,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);
