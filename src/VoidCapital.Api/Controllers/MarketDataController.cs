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
    public async Task<ActionResult<ApiResponse<decimal>>> GetPrice(string symbol, CancellationToken ct)
    {
        // M1: null/empty/whitespace symbol must be a 400, not a 500 from
        // ToUpperInvariant() on null or a pointless service round-trip.
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ValidationException("Symbol is required.");

        var price = await _marketData.GetCurrentPriceAsync(symbol.Trim().ToUpperInvariant(), ct);
        return Ok(ApiResponse<decimal>.Ok(price));
    }

    [HttpGet("{symbol}/history")]
    public async Task<ActionResult<ApiResponse<IEnumerable<StockPrice>>>> GetHistory(
        string symbol, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ValidationException("Symbol is required.");

        var fromDate = from ?? DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-1));
        var toDate = to ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var history = await _marketData.GetPriceHistoryAsync(symbol.Trim().ToUpperInvariant(), fromDate, toDate, ct);
        return Ok(ApiResponse<IEnumerable<StockPrice>>.Ok(history));
    }
}
