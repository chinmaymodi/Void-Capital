namespace VoidCapital.Api.Modules.Portfolio.Models;

/// <summary>
/// A position in one instrument. Domain property names mirror the DB columns
/// (quantity / avg_price); the API contract exposes shares / avgBuyPrice via
/// <see cref="DTOs.HoldingDto"/>.
/// </summary>
public class Holding
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string InstrumentType { get; set; } = "EQ";
    public string Symbol { get; set; } = string.Empty;
    public DateOnly? Expiry { get; set; }
    public decimal? Strike { get; set; }
    public int Quantity { get; set; }
    public decimal AvgPrice { get; set; }
    public DateOnly BuyDate { get; set; }
}
