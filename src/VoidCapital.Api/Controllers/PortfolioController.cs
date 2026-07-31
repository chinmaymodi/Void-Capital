using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class PortfolioController : ControllerBase
{
    private readonly IPortfolioService _portfolioService;

    public PortfolioController(IPortfolioService portfolioService)
    {
        _portfolioService = portfolioService;
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<ApiResponse<PortfolioStateDto>>> GetState(int userId)
    {
        var state = await _portfolioService.GetPortfolioStateAsync(userId);
        return Ok(ApiResponse<PortfolioStateDto>.Ok(state));
    }

    [HttpGet("{userId:int}/history")]
    public async Task<ActionResult<ApiResponse<IEnumerable<PnlSnapshot>>>> GetHistory(int userId)
    {
        var history = await _portfolioService.GetPnlHistoryAsync(userId);
        return Ok(ApiResponse<IEnumerable<PnlSnapshot>>.Ok(history));
    }
}
