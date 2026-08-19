namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>One portfolio's side in the three-column comparison.</summary>
public record ComparisonPortfolioDto(
    int UserId,
    string Name,
    decimal Cash,
    decimal HoldingsValue,
    decimal TotalValue,
    decimal TotalReturn,
    decimal TotalReturnPercent,
    decimal StartingBudget);

/// <summary>Gap between a pair of portfolios, measured in rupees and percent.</summary>
public record ComparisonGapDto(
    string Leader,
    string Trailer,
    decimal GapRupees,
    decimal GapPercent);

/// <summary>
/// Response for <c>GET /api/v1/performance/compare</c>: the portfolios involved
/// plus pairwise gap metrics. TotalReturn is relative to the user's starting
/// budget.
/// </summary>
public record PortfolioComparisonDto(
    IEnumerable<ComparisonPortfolioDto> Portfolios,
    IEnumerable<ComparisonGapDto> Gaps);
