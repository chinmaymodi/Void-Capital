using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/market")]
public class MarketDataController : ControllerBase
{
    private readonly IMarketDataService _marketData;

    public MarketDataController(IMarketDataService marketData)
    {
        _marketData = marketData;
    }

    [HttpGet("{symbol}/price")]
    public async Task<ActionResult<ApiResponse<decimal>>> GetPrice(string symbol)
    {
        var price = await _marketData.GetCurrentPriceAsync(symbol.ToUpperInvariant());
        return Ok(ApiResponse<decimal>.Ok(price));
    }

    [HttpGet("{symbol}/history")]
    public async Task<ActionResult<ApiResponse<IEnumerable<StockPrice>>>> GetHistory(
        string symbol, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var history = await _marketData.GetPriceHistoryAsync(symbol.ToUpperInvariant(), fromDate, toDate);
        return Ok(ApiResponse<IEnumerable<StockPrice>>.Ok(history));
    }
}
