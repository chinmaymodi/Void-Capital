namespace VoidCapital.Api.Modules.Portfolio.Models;

public class WatchlistItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public DateOnly AddedDate { get; set; }
}