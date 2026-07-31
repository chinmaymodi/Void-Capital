namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>Request body for POST /api/v1/holdings/{userId}/buy and /sell.</summary>
public record TradeRequest(string Symbol, int Shares);
