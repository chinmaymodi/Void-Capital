namespace VoidCapital.Api.Modules.Portfolio.Models;

/// <summary>
/// One run of the daily cycle (ops.cycle_runs). Records when the cycle ran,
/// whether it succeeded, and what it produced so the admin panel can show run
/// history rather than relying on log scraping.
/// </summary>
public class CycleRun
{
    public int Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Status { get; set; } = "RUNNING"; // RUNNING / SUCCEEDED / FAILED
    public string? Error { get; set; }
    public int SignalsGenerated { get; set; }
    public int SignalsExecuted { get; set; }
    public int UsersProcessed { get; set; }
}
