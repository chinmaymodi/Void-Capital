namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>One user's balance line in the admin status report.</summary>
public record UserBalanceDto(
    int UserId,
    string Name,
    decimal CurrentCash,
    decimal TotalValue,
    decimal TotalReturn,
    decimal TotalReturnPercent);

/// <summary>
/// Response for <c>GET /api/v1/admin/status</c>: system time, pending signal
/// count, and a per-user balance report.
/// </summary>
public record AdminStatusDto(
    DateTime UtcNow,
    int PendingSignalCount,
    IEnumerable<UserBalanceDto> Users);

/// <summary>Result of <c>POST /api/v1/admin/square-off/{userId}</c>.</summary>
public record SquareOffResultDto(
    int UserId,
    int PositionsSold,
    decimal Proceeds,
    decimal RemainingCash);

/// <summary>
/// Body for <c>PUT /api/v1/admin/settings/global</c>. Applied to every user's
/// settings row (there is no dedicated global-config table).
/// </summary>
public record GlobalSettingsRequest(
    decimal MinConfidence,
    string[] Watchlist);
