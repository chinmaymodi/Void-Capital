namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>
/// Ingest request body for <c>POST /api/v1/admin/ingest-signals</c>. Mirrors
/// the model_predictions columns; userId is required (400 when missing).
/// InstrumentType defaults to "EQ" when omitted; expiry/strike are required
/// for options instruments (CE/PE).
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
    decimal? StopLoss,
    string? InstrumentType = "EQ",
    DateOnly? Expiry = null,
    decimal? Strike = null);
