namespace VoidCapital.Api.Modules.Portfolio.Models;

/// <summary>
/// Per-user trading configuration from identity.settings. Drives margin
/// (negative_limit), auto-execution, and signal confidence thresholds.
/// </summary>
public class UserSettings
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool AutoExecute { get; set; }
    public decimal MinConfidence { get; set; }
    public decimal NegativeLimit { get; set; }
    public decimal InterestRate { get; set; }
    public string Watchlist { get; set; } = "[]";
}
