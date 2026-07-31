namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>Trade as seen by the frontend (shares/total naming per API contract).</summary>
public record TradeDto(
    int Id,
    string Symbol,
    string Type,
    int Shares,
    decimal Price,
    decimal Total,
    string? Reason,
    DateTime Timestamp);
