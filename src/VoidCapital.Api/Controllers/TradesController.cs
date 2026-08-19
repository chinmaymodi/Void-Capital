using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class TradesController : ControllerBase
{
    private readonly ITradeRepository _tradeRepo;

    public TradesController(ITradeRepository tradeRepo)
    {
        _tradeRepo = tradeRepo;
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<ApiResponse<PagedResult<TradeDto>>>> GetTrades(
        int userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? symbol = null,
        [FromQuery] string? type = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        if (!User.CanAccess(userId)) return Forbid();
        // T2: clamp BEFORE building the query so an unbounded pageSize never
        // reaches the repository (the response echoes the same clamped values).
        var safePage = Math.Max(page, 1);
        var clampedPageSize = Math.Clamp(pageSize, 1, 100);
        var query = new TradeQuery
        {
            Page = safePage,
            PageSize = clampedPageSize,
            Symbol = symbol,
            Type = type,
            From = from,
            To = to
        };

        var (items, total) = await _tradeRepo.QueryAsync(userId, query);

        var result = new PagedResult<TradeDto>(
            items.Select(ToDto),
            total,
            safePage,
            clampedPageSize);
        return Ok(ApiResponse<PagedResult<TradeDto>>.Ok(result));
    }

    [HttpGet("{userId:int}/export")]
    public async Task<IActionResult> ExportTrades(
        int userId,
        [FromQuery] string? symbol = null,
        [FromQuery] string? type = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        if (!User.CanAccess(userId)) return Forbid();
        // Export everything matching the filters (no paging).
        var query = new TradeQuery
        {
            Page = 1,
            PageSize = int.MaxValue,
            Symbol = symbol,
            Type = type,
            From = from,
            To = to
        };

        var (items, _) = await _tradeRepo.QueryAsync(userId, query);

        var csv = new StringBuilder();
        csv.AppendLine("id,symbol,type,quantity,price,total_value,commission,reason,timestamp");
        foreach (var t in items)
        {
            csv.AppendLine(string.Join(',',
                t.Id.ToString(CultureInfo.InvariantCulture),
                CsvEscape(t.Symbol),
                CsvEscape(t.Type),
                t.Quantity.ToString(CultureInfo.InvariantCulture),
                t.Price.ToString(CultureInfo.InvariantCulture),
                t.TotalValue.ToString(CultureInfo.InvariantCulture),
                t.Commission.ToString(CultureInfo.InvariantCulture),
                CsvEscape(t.Reason ?? string.Empty),
                t.Timestamp.ToString("O", CultureInfo.InvariantCulture)));
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"trades_{userId}_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string CsvEscape(string value)
    {
        // T3: Excel formula injection guard. Cells starting with a formula or
        // command character (=, +, -, @, tab) are prefixed with a single quote
        // so opening the CSV in Excel renders them as text instead of
        // executing them. Applied before quoting so the prefix survives.
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t')
            value = "'" + value;

        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static TradeDto ToDto(Trade trade) =>
        new(trade.Id, trade.Symbol, trade.Type, trade.Quantity, trade.Price,
            trade.TotalValue, trade.Commission, trade.Reason, trade.Timestamp);
}
