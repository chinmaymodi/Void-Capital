namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>
/// Signal as seen by the frontend. status is serialized as the enum name
/// (PENDING/APPROVED/...); entry/target/stop come from the linked
/// signal_performance row when present.
/// </summary>
public record SignalDto(
    int Id,
    DateOnly Date,
    string Symbol,
    string Action,
    decimal Confidence,
    string? Reason,
    string ModelName,
    string Status,
    int? SuggestedQuantity,
    decimal? EntryPrice,
    decimal? TargetPrice,
    decimal? StopLoss,
    string? FailureReason,
    string InstrumentType = "EQ",
    DateOnly? Expiry = null,
    decimal? Strike = null)
{
    public static SignalDto From(Signal signal) => new(
        signal.Id,
        signal.Date,
        signal.Symbol,
        signal.Action,
        signal.Confidence,
        signal.Reason,
        signal.ModelName,
        signal.Status.ToString(),
        signal.SuggestedQuantity,
        signal.Performance?.EntryPrice,
        signal.Performance?.TargetPrice,
        signal.Performance?.StopLoss,
        signal.FailureReason,
        signal.InstrumentType,
        signal.Expiry,
        signal.Strike);
}
