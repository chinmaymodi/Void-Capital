using VoidCapital.Api.Modules.Signals.Models;

namespace VoidCapital.Api.Modules.Signals;

/// <summary>
/// A model prediction stored in <c>signals.model_predictions</c>. Created by
/// the admin ingest endpoint, then moves through the approval workflow.
/// </summary>
public class Signal
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public DateOnly Date { get; set; }
    public string InstrumentType { get; set; } = "EQ";
    public string Symbol { get; set; } = string.Empty;
    public DateOnly? Expiry { get; set; }
    public decimal? Strike { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty; // BUY | SELL | HOLD
    public decimal Confidence { get; set; }
    public string? Reason { get; set; }
    public int? SuggestedQuantity { get; set; }
    public SignalStatus Status { get; set; } = SignalStatus.PENDING;
    public string? FailureReason { get; set; }

    /// <summary>Linked performance tracking row (created at ingest).</summary>
    public SignalPerformance? Performance { get; set; }
}
