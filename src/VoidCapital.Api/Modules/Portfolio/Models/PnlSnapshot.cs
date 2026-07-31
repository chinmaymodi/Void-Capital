namespace VoidCapital.Api.Modules.Portfolio.Models;

public class PnlSnapshot
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public decimal PortfolioValue { get; set; }
    public decimal CashValue { get; set; }
    public decimal HoldingsValue { get; set; }
}
