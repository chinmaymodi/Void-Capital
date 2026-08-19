namespace VoidCapital.Api.Modules.Portfolio.Models;

public class Trade
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string InstrumentType { get; set; } = "EQ";
    public string Symbol { get; set; } = string.Empty;
    public DateOnly? Expiry { get; set; }
    public decimal? Strike { get; set; }
    public string Type { get; set; } = string.Empty; // BUY | SELL
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal TotalValue { get; set; }
    public decimal Commission { get; set; }
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; }
}
