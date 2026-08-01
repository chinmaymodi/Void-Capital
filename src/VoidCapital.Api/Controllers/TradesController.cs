using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class TradesController : ControllerBase
{
    private readonly ITradeRepository _tradeRepo;

    public TradesController(ITradeRepository tradeRepo)
    {
        _tradeRepo = tradeRepo;
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<ApiResponse<object>>> GetTrades(
        int userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? symbol = null,
        [FromQuery] string? type = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
        var query = new TradeQuery
        {
            Page = page,
            PageSize = pageSize,
            Symbol = symbol,
            Type = type,
            From = from,
            To = to
        };

        var (items, total) = await _tradeRepo.QueryAsync(userId, query);

        var result = new
        {
            items = items.Select(ToDto),
            total,
            page = Math.Max(page, 1),
            pageSize = Math.Clamp(pageSize, 1, 100)
        };
        return Ok(ApiResponse<object>.Ok(result));
    }

    [HttpGet("{userId:int}/export")]
    public async Task<IActionResult> ExportTrades(
        int userId,
        [FromQuery] string? symbol = null,
        [FromQuery] string? type = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null)
    {
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
        csv.AppendLine("id,symbol,type,quantity,price,total_value,reason,timestamp");
        foreach (var t in items)
        {
            csv.AppendLine(string.Join(',',
                t.Id.ToString(CultureInfo.InvariantCulture),
                CsvEscape(t.Symbol),
                CsvEscape(t.Type),
                t.Quantity.ToString(CultureInfo.InvariantCulture),
                t.Price.ToString(CultureInfo.InvariantCulture),
                t.TotalValue.ToString(CultureInfo.InvariantCulture),
                CsvEscape(t.Reason ?? string.Empty),
                t.Timestamp.ToString("O", CultureInfo.InvariantCulture)));
        }

        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        return File(bytes, "text/csv; charset=utf-8", $"trades_{userId}_{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    private static string CsvEscape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static TradeDto ToDto(Trade trade) =>
        new(trade.Id, trade.Symbol, trade.Type, trade.Quantity, trade.Price,
            trade.TotalValue, trade.Reason, trade.Timestamp);
}
