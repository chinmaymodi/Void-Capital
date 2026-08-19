using System.ComponentModel.DataAnnotations;

namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>
/// Request body for POST /api/v1/holdings/{userId}/buy and /sell.
/// Validation metadata lives on the primary constructor parameters (this
/// ASP.NET Core version requires it there, not on properties - see F10).
/// </summary>
public record TradeRequest(
    [Required(AllowEmptyStrings = false)] string Symbol,
    [Range(1, int.MaxValue)] int Shares);