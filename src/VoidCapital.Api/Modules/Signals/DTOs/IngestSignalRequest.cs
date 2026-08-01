namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>
/// Ingest request body for <c>POST /api/v1/admin/ingest-signals</c>. Mirrors
/// the model_predictions columns; userId is required (400 when missing).
/// </summary>
public record IngestSignalRequest(
    int? UserId,
    string Symbol,
    string Action,
    decimal Confidence,
    string? Reason,
    string ModelName,
    int? SuggestedQuantity,
    decimal? EntryPrice,
    decimal? TargetPrice,
    decimal? StopLoss);
