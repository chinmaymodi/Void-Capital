namespace VoidCapital.Api.Shared.Repositories;

/// <summary>Filters + paging for the trade log query (D5.1).</summary>
public class TradeQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? Symbol { get; set; }
    public string? Type { get; set; } // BUY | SELL
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
}
