using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Signals.DTOs;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
// Cross-user dashboard data (every agent's value/P&L, model aggregates,
// compare) - admin only. A regular user has no per-user performance endpoint.
[Authorize(Roles = "Admin")]
public class PerformanceController : ControllerBase
{
    private readonly ISignalRepository _signalRepo;
    private readonly IUserRepository _userRepo;
    private readonly IPortfolioService _portfolioService;

    public PerformanceController(
        ISignalRepository signalRepo,
        IUserRepository userRepo,
        IPortfolioService portfolioService)
    {
        _signalRepo = signalRepo;
        _userRepo = userRepo;
        _portfolioService = portfolioService;
    }

    /// <summary>Per-model aggregates over resolved signal performance rows.</summary>
    [HttpGet("models")]
    public async Task<ActionResult<ApiResponse<IEnumerable<ModelPerformanceDto>>>> GetModels()
    {
        var stats = await _signalRepo.GetModelPerformanceAsync();
        return Ok(ApiResponse<IEnumerable<ModelPerformanceDto>>.Ok(stats));
    }

    /// <summary>
    /// Paged list of resolved signals. Optional userId/model filters; the
    /// response shape matches the trade log contract (items + total + page).
    /// </summary>
    [HttpGet("signals")]
    public async Task<ActionResult<ApiResponse<PagedResult<ResolvedSignalDto>>>> GetSignals(
        [FromQuery] int? userId = null,
        [FromQuery] string? model = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var safePage = Math.Max(page, 1);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var query = new PerformanceQuery(userId, model, safePage, clampedPageSize);
        var (items, total) = await _signalRepo.GetResolvedAsync(query);

        var result = new PagedResult<ResolvedSignalDto>(items, total, safePage, clampedPageSize);
        return Ok(ApiResponse<PagedResult<ResolvedSignalDto>>.Ok(result));
    }

    /// <summary>
    /// Side-by-side comparison across all users: portfolio state, total return
    /// vs starting budget, and pairwise gaps (rupees + percent).
    /// </summary>
    [HttpGet("compare")]
    public async Task<ActionResult<ApiResponse<PortfolioComparisonDto>>> GetCompare()
    {
        var users = await _userRepo.GetAllAsync();

        var portfolios = new List<ComparisonPortfolioDto>();
        foreach (var user in users)
        {
            var state = await _portfolioService.GetPortfolioStateAsync(user.Id);
            var totalReturn = state.TotalValue - user.StartingBudget;

            portfolios.Add(new ComparisonPortfolioDto(
                user.Id,
                user.Name,
                state.Cash,
                state.HoldingsValue,
                state.TotalValue,
                totalReturn,
                user.StartingBudget > 0 ? totalReturn / user.StartingBudget : 0m,
                user.StartingBudget));
        }

        var gaps = new List<ComparisonGapDto>();
        for (var i = 0; i < portfolios.Count; i++)
        {
            for (var j = i + 1; j < portfolios.Count; j++)
            {
                var left = portfolios[i];
                var right = portfolios[j];
                var leader = left.TotalValue >= right.TotalValue ? left : right;
                var trailer = left.TotalValue >= right.TotalValue ? right : left;

                gaps.Add(new ComparisonGapDto(
                    leader.Name,
                    trailer.Name,
                    leader.TotalValue - trailer.TotalValue,
                    trailer.TotalValue > 0
                        ? (leader.TotalValue - trailer.TotalValue) / trailer.TotalValue
                        : 0m));
            }
        }

        var comparison = new PortfolioComparisonDto(portfolios, gaps);
        return Ok(ApiResponse<PortfolioComparisonDto>.Ok(comparison));
    }
}
