namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>User settings as seen by the frontend. Watchlist is a JSON array string in the DB.</summary>
public record SettingsDto(
    int Id,
    int UserId,
    bool AutoExecute,
    decimal MinConfidence,
    decimal NegativeLimit,
    decimal InterestRate,
    string[] Watchlist);

/// <summary>Request body for PUT /api/v1/settings/{userId}.</summary>
public record UpdateSettingsRequest(
    bool AutoExecute,
    decimal MinConfidence,
    decimal NegativeLimit,
    decimal InterestRate,
    string[] Watchlist);
