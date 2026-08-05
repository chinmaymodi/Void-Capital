using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio.Models;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// Portfolio buy/sell against real PostgreSQL: real market data row, cash
/// deduction, holding upsert, trade log entry.
/// </summary>
[Collection("integration")]
public class PortfolioIntegrationTests : IDisposable
{
    private readonly IntegrationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<int> _createdUsers = new();

    public PortfolioIntegrationTests(IntegrationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
        foreach (var userId in _createdUsers)
            CleanupUserAsync(userId).GetAwaiter().GetResult();
        _client.Dispose();
    }

    private async Task CleanupUserAsync(int userId)
    {
        await using var db = await _factory.CreateDbAsync();
        await db.Database.ExecuteSqlRawAsync("""
            DELETE FROM portfolio.trade_log WHERE user_id = {0};
            DELETE FROM portfolio.holdings WHERE user_id = {0};
            DELETE FROM portfolio.pnl_snapshots WHERE user_id = {0};
            DELETE FROM portfolio.watchlist WHERE user_id = {0};
            DELETE FROM identity.settings WHERE user_id = {0};
            DELETE FROM identity.users WHERE id = {0};
            """, userId);
    }

    private async Task<int> CreateUserAsync(decimal cash = 100000m)
    {
        var user = new User
        {
            Name = "IT Portfolio",
            Email = $"it-portfolio-{Guid.NewGuid():N}@voidcapital.test",
            StartingBudget = cash,
            CurrentCash = cash,
            CreatedAt = DateTime.UtcNow
        };
        await using var db = await _factory.CreateDbAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserSettings.Add(new UserSettings
        {
            UserId = user.Id, AutoExecute = false, MinConfidence = 0.5m,
            NegativeLimit = 0m, InterestRate = 0m, Watchlist = "[]"
        });
        await db.SaveChangesAsync();
        _createdUsers.Add(user.Id);
        return user.Id;
    }

    /// <summary>
    /// Seeds a stock price for today. The stocks PK is (symbol, date), so any
    /// prior rows for this symbol/today are removed first to keep the seed
    /// idempotent across test runs.
    /// </summary>
    private async Task SeedStockAsync(string symbol, decimal price)
    {
        await using var db = await _factory.CreateDbAsync();
        await db.StockPrices
            .Where(s => s.Symbol == symbol && s.Date == DateOnly.FromDateTime(DateTime.UtcNow))
            .ExecuteDeleteAsync();
        db.StockPrices.Add(new StockPrice(
            symbol, DateOnly.FromDateTime(DateTime.UtcNow), price, price, price, price, 10000));
        await db.SaveChangesAsync();
    }

    private record TradeDto(int Id, string Symbol, string Type, int Shares, decimal Price, decimal Total, string? Reason, DateTime Timestamp);
    private record TestEnvelope<T>(bool Success, T? Data, string? Error, string? TraceId);

    [Fact]
    public async Task Buy_DeductsCash_CreatesHolding_AndLogsTrade()
    {
        var userId = await CreateUserAsync(cash: 100000m);
        await SeedStockAsync("ITST_BUY", 1000m);

        var response = await _client.PostAsJsonAsync($"/api/v1/holdings/{userId}/buy",
            new { symbol = "ITST_BUY", shares = 10 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<TradeDto>>();
        envelope!.Data!.Symbol.Should().Be("ITST_BUY");
        envelope.Data.Type.Should().Be("BUY");
        envelope.Data.Shares.Should().Be(10);

        await using var db = await _factory.CreateDbAsync();
        var user = await db.Users.FindAsync(userId);
        user!.CurrentCash.Should().Be(90000m); // 100000 - 10*1000

        var holding = await db.Holdings.FirstOrDefaultAsync(h => h.UserId == userId);
        holding.Should().NotBeNull();
        holding!.Quantity.Should().Be(10);
        holding.AvgPrice.Should().Be(1000m);

        var trade = await db.Trades.FirstOrDefaultAsync(t => t.UserId == userId);
        trade.Should().NotBeNull();
        trade!.TotalValue.Should().Be(10000m);
    }

    [Fact]
    public async Task Sell_AddsCash_AndReducesHolding()
    {
        var userId = await CreateUserAsync(cash: 100000m);
        await SeedStockAsync("ITST_SELL", 1000m);
        await _client.PostAsJsonAsync($"/api/v1/holdings/{userId}/buy",
            new { symbol = "ITST_SELL", shares = 10 });

        var response = await _client.PostAsJsonAsync($"/api/v1/holdings/{userId}/sell",
            new { symbol = "ITST_SELL", shares = 4 });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db = await _factory.CreateDbAsync();
        var user = await db.Users.FindAsync(userId);
        // 100000 - 10000 (buy) + 4000 (sell proceeds) = 94000
        user!.CurrentCash.Should().Be(94000m);

        var holding = await db.Holdings.FirstOrDefaultAsync(h => h.UserId == userId);
        holding!.Quantity.Should().Be(6);
    }

    [Fact]
    public async Task Buy_WithInsufficientCash_Returns400()
    {
        var userId = await CreateUserAsync(cash: 1000m);
        await SeedStockAsync("ITST_DEAR", 500m);

        var response = await _client.PostAsJsonAsync($"/api/v1/holdings/{userId}/buy",
            new { symbol = "ITST_DEAR", shares = 10 }); // cost 5000 > cash 1000

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<object>>();
        envelope!.Success.Should().BeFalse();
        envelope.Error.Should().Contain("Insufficient funds");
    }

    [Fact]
    public async Task Sell_MoreThanOwned_Returns400()
    {
        var userId = await CreateUserAsync(cash: 100000m);
        await SeedStockAsync("ITST_SMALL", 100m);
        await _client.PostAsJsonAsync($"/api/v1/holdings/{userId}/buy",
            new { symbol = "ITST_SMALL", shares = 2 });

        var response = await _client.PostAsJsonAsync($"/api/v1/holdings/{userId}/sell",
            new { symbol = "ITST_SMALL", shares = 5 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetPortfolio_ReturnsCashAndHoldingsValue()
    {
        var userId = await CreateUserAsync(cash: 100000m);
        await SeedStockAsync("ITST_STATE", 100m);
        await _client.PostAsJsonAsync($"/api/v1/holdings/{userId}/buy",
            new { symbol = "ITST_STATE", shares = 10 }); // 1000 spent

        var response = await _client.GetFromJsonAsync<TestEnvelope<PortfolioStateDto>>(
            $"/api/v1/portfolio/{userId}");

        response!.Data!.Cash.Should().Be(99000m);
        response.Data.HoldingsValue.Should().Be(1000m); // 10 * 100
        response.Data.TotalValue.Should().Be(100000m);
    }

    private record PortfolioStateDto(decimal Cash, decimal HoldingsValue, decimal TotalValue);
}
