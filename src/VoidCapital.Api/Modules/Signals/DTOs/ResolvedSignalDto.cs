namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>
/// A resolved signal row: the signal joined with its settled performance
/// tracking entry (outcome is HIT_TARGET | HIT_STOP | EXPIRED).
/// </summary>
public record ResolvedSignalDto(
    int SignalId,
    DateOnly Date,
    string Symbol,
    string Action,
    string ModelName,
    decimal EntryPrice,
    decimal? TargetPrice,
    decimal? ExitPrice,
    string Outcome,
    decimal? ActualReturn,
    DateTime? ResolvedAt,
    int EvaluationDays);
