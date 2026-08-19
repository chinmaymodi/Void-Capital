namespace VoidCapital.Api.Modules.Portfolio.Models;

/// <summary>
/// Per-user trading configuration from identity.settings. Drives margin
/// (negative_limit), auto-execution, and signal confidence thresholds.
/// IsHalted marks a terminal agent (F12): the daily cycle stops generating,
/// executing, charging interest, and margin-calling until an admin revives it.
/// </summary>
public class UserSettings
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool AutoExecute { get; set; }
    public bool IsHalted { get; set; }
    public decimal MinConfidence { get; set; }
    public decimal NegativeLimit { get; set; }
    public decimal InterestRate { get; set; }
    public string Watchlist { get; set; } = "[]";
}
