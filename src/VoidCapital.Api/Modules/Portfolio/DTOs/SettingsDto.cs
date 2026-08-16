using System.ComponentModel.DataAnnotations;

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

/// <summary>
/// Request body for PUT /api/v1/settings/{userId}.
/// InterestRate is an ANNUAL rate accrued daily (cash * rate / 365) on
/// negative cash; 0.1825 = 18.25%/yr = 0.05%/day. MinConfidence is the
/// approval-gate threshold in [0,1]; NegativeLimit is the margin credit line
/// in rupees (0 = none). Both SettingsController and AdminController share
/// this contract; [ApiController] model validation rejects out-of-range
/// values with 400 before any persistence.
/// </summary>
public record UpdateSettingsRequest(
    bool AutoExecute,
    [Range(0.0, 1.0)] decimal MinConfidence,
    [Range(0.0, double.MaxValue)] decimal NegativeLimit,
    [Range(0.0, 0.5)] decimal InterestRate,
    string[] Watchlist);
