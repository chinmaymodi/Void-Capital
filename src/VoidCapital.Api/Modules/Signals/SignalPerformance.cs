namespace VoidCapital.Api.Modules.Signals;

/// <summary>
/// Performance tracking row in <c>signals.signal_performance</c>. Created at
/// ingest time with entry/target/stop prices; resolved after the evaluation
/// horizon by <c>SignalPerformanceService</c>.
/// </summary>
public class SignalPerformance
{
    public int Id { get; set; }
    public int SignalId { get; set; }
    public Signal? Signal { get; set; }

    public decimal EntryPrice { get; set; }
    public decimal? TargetPrice { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? ExitPrice { get; set; }

    /// <summary>PENDING | HIT_TARGET | HIT_STOP | EXPIRED</summary>
    public string? Outcome { get; set; }
    public decimal? ActualReturn { get; set; }
    public int EvaluationDays { get; set; } = 5;
    public DateTime CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
}
