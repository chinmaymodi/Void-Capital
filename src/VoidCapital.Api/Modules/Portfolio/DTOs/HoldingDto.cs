namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>Holding as seen by the frontend: current price + unrealized P&L.</summary>
public record HoldingDto(
    int Id,
    string Symbol,
    int Shares,
    decimal AvgBuyPrice,
    decimal CurrentPrice,
    decimal UnrealizedPnl,
    decimal PercentOfPortfolio);
