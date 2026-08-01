namespace VoidCapital.Api.Modules.Signals.Models;

/// <summary>
/// Lifecycle of a signal row in <c>signals.model_predictions</c>. Stored as a
/// string in the <c>status</c> column; PENDING is the only mutable state,
/// APPROVED/REJECTED are terminal (EXECUTED/FAILED are set by auto-execute).
/// </summary>
public enum SignalStatus
{
    PENDING,
    APPROVED,
    REJECTED,
    EXECUTED,
    FAILED
}
