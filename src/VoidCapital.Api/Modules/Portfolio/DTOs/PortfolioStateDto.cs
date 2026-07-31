namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>Portfolio state: cash + holdings value + total. Immutable record.</summary>
public record PortfolioStateDto(decimal Cash, decimal HoldingsValue, decimal TotalValue);
