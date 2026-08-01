namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>
/// Aggregate performance statistics per model, computed from resolved
/// <c>signals.signal_performance</c> rows. WinRate is 0..1 (hit-target /
/// resolved-signals ratio); Avg/Best/Worst returns are relative to entry price.
/// </summary>
public record ModelPerformanceDto(
    string ModelName,
    int TotalSignals,
    int ResolvedSignals,
    int HitTargetCount,
    decimal WinRate,
    decimal AvgReturn,
    decimal? BestReturn,
    decimal? WorstReturn);
