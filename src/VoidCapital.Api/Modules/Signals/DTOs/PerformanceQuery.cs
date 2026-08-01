namespace VoidCapital.Api.Modules.Signals.DTOs;

/// <summary>
/// Filters for <c>GET /api/v1/performance/signals</c>. Resolved signals can be
/// scoped to a user and/or model; paging mirrors the trade log contract.
/// </summary>
public record PerformanceQuery(
    int? UserId = null,
    string? Model = null,
    int Page = 1,
    int PageSize = 20);
