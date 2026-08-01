namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>Per-item result for batch approve/reject operations.</summary>
public record SignalBatchResult(int Id, bool Success, string? Error)
{
    public static SignalBatchResult Ok(int id) => new(id, true, null);

    public static SignalBatchResult Failed(int id, string error) => new(id, false, error);
}
