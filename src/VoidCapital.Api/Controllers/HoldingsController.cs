using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class HoldingsController : ControllerBase
{
    private readonly IPortfolioService _portfolioService;

    public HoldingsController(IPortfolioService portfolioService)
    {
        _portfolioService = portfolioService;
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<ApiResponse<IEnumerable<HoldingDto>>>> GetHoldings(int userId)
    {
        var holdings = await _portfolioService.GetHoldingsAsync(userId);
        return Ok(ApiResponse<IEnumerable<HoldingDto>>.Ok(holdings));
    }

    [HttpPost("{userId:int}/buy")]
    public async Task<ActionResult<ApiResponse<TradeDto>>> Buy(int userId, [FromBody] TradeRequest request)
    {
        var trade = await _portfolioService.ExecuteBuyAsync(userId, request.Symbol, request.Shares);
        return Ok(ApiResponse<TradeDto>.Ok(ToDto(trade)));
    }

    [HttpPost("{userId:int}/sell")]
    public async Task<ActionResult<ApiResponse<TradeDto>>> Sell(int userId, [FromBody] TradeRequest request)
    {
        var trade = await _portfolioService.ExecuteSellAsync(userId, request.Symbol, request.Shares);
        return Ok(ApiResponse<TradeDto>.Ok(ToDto(trade)));
    }

    private static TradeDto ToDto(Trade trade) =>
        new(trade.Id, trade.Symbol, trade.Type, trade.Quantity, trade.Price,
            trade.TotalValue, trade.Reason, trade.Timestamp);
}
