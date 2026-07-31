namespace VoidCapital.Api.Modules.Portfolio.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public decimal StartingBudget { get; set; }
    public decimal CurrentCash { get; set; }
    public DateTime CreatedAt { get; set; }
}
