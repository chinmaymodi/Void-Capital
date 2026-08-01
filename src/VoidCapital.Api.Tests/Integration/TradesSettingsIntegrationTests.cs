using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// Trade log and settings endpoints against real PostgreSQL: seeded rows are
/// queried with paging/filters, exported as CSV, and settings round-trip.
/// </summary>
[Collection("integration")]
public class TradesSettingsIntegrationTests : IDisposable
{
    private readonly IntegrationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<int> _createdUsers = new();

    public TradesSettingsIntegrationTests(IntegrationFactory factory)
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
            DELETE FROM identity.settings WHERE user_id = {0};
            DELETE FROM identity.users WHERE id = {0};
            """, userId);
    }

    private async Task<int> CreateUserWithTradesAsync()
    {
        var user = new User
        {
            Name = "IT Trades",
            Email = $"it-trades-{Guid.NewGuid():N}@voidcapital.test",
            StartingBudget = 100000m,
            CurrentCash = 50000m,
            CreatedAt = DateTime.UtcNow
        };
        await using var db = await _factory.CreateDbAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        _createdUsers.Add(user.Id);

        // 3 trades: 2 RELIANCE buys + 1 TCS sell, distinct dates.
        db.Trades.AddRange(
            new Trade
            {
                UserId = user.Id, Symbol = "RELIANCE", Type = "BUY", Quantity = 10,
                Price = 2850m, TotalValue = 28500m, Reason = "first",
                Timestamp = new DateTime(2026, 7, 15, 10, 0, 0, DateTimeKind.Utc)
            },
            new Trade
            {
                UserId = user.Id, Symbol = "TCS", Type = "SELL", Quantity = 3,
                Price = 3800m, TotalValue = 11400m, Reason = "trim",
                Timestamp = new DateTime(2026, 7, 20, 11, 30, 0, DateTimeKind.Utc)
            },
            new Trade
            {
                UserId = user.Id, Symbol = "RELIANCE", Type = "BUY", Quantity = 5,
                Price = 2880m, TotalValue = 14400m, Reason = "second",
                Timestamp = new DateTime(2026, 7, 25, 9, 45, 0, DateTimeKind.Utc)
            });
        await db.SaveChangesAsync();
        return user.Id;
    }

    private record PagedTrades<T>(List<T> Items, int Total, int Page, int PageSize);
    private record TradeDto(int Id, string Symbol, string Type, int Shares, decimal Price, decimal Total, string? Reason, DateTime Timestamp);
    private record TestEnvelope<T>(bool Success, T? Data, string? Error, string? TraceId);

    [Fact]
    public async Task GetTrades_ReturnsAllWithTotal()
    {
        var userId = await CreateUserWithTradesAsync();

        var response = await _client.GetAsync($"/api/v1/trades/{userId}?page=1&pageSize=10");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<PagedTrades<TradeDto>>>();
        envelope!.Data!.Total.Should().Be(3);
        envelope.Data.Items.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetTrades_FiltersBySymbolAndType()
    {
        var userId = await CreateUserWithTradesAsync();

        var response = await _client.GetAsync($"/api/v1/trades/{userId}?symbol=RELIANCE&type=BUY");
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<PagedTrades<TradeDto>>>();

        envelope!.Data!.Total.Should().Be(2);
        envelope.Data.Items.Should().OnlyContain(t => t.Symbol == "RELIANCE" && t.Type == "BUY");
    }

    [Fact]
    public async Task GetTrades_PaginatesCorrectly()
    {
        var userId = await CreateUserWithTradesAsync();

        var page1 = await _client.GetFromJsonAsync<TestEnvelope<PagedTrades<TradeDto>>>(
            $"/api/v1/trades/{userId}?page=1&pageSize=2");
        var page2 = await _client.GetFromJsonAsync<TestEnvelope<PagedTrades<TradeDto>>>(
            $"/api/v1/trades/{userId}?page=2&pageSize=2");

        page1!.Data!.Items.Should().HaveCount(2);
        page1.Data.Total.Should().Be(3);
        page2!.Data!.Items.Should().HaveCount(1);
        page2.Data.Total.Should().Be(3);
        // Newest first.
        page1.Data.Items[0].Reason.Should().Be("second");
        page2.Data.Items[0].Reason.Should().Be("first");
    }

    [Fact]
    public async Task GetTrades_FiltersByDateRange()
    {
        var userId = await CreateUserWithTradesAsync();

        var response = await _client.GetAsync(
            $"/api/v1/trades/{userId}?from=2026-07-18&to=2026-07-22");
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<PagedTrades<TradeDto>>>();

        envelope!.Data!.Total.Should().Be(1);
        envelope.Data.Items.Single().Symbol.Should().Be("TCS");
    }

    [Fact]
    public async Task ExportTrades_ReturnsCsvWithAllRows()
    {
        var userId = await CreateUserWithTradesAsync();

        var response = await _client.GetAsync($"/api/v1/trades/{userId}/export");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var csv = await response.Content.ReadAsStringAsync();
        csv.Should().StartWith("id,symbol,type,quantity,price,total_value,reason,timestamp");
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(4); // header + 3 trades
        csv.Should().Contain("RELIANCE,BUY,10");
    }

    [Fact]
    public async Task Settings_PutGet_RoundTrips()
    {
        var user = new User
        {
            Name = "IT Settings",
            Email = $"it-settings-{Guid.NewGuid():N}@voidcapital.test",
            StartingBudget = 100000m,
            CurrentCash = 100000m,
            CreatedAt = DateTime.UtcNow
        };
        await using var db0 = await _factory.CreateDbAsync();
        db0.Users.Add(user);
        await db0.SaveChangesAsync();
        _createdUsers.Add(user.Id);
        db0.UserSettings.Add(new UserSettings
        {
            UserId = user.Id, AutoExecute = false, MinConfidence = 0.5m,
            NegativeLimit = 0m, InterestRate = 0m, Watchlist = "[]"
        });
        await db0.SaveChangesAsync();

        // PUT new values.
        var putResponse = await _client.PutAsJsonAsync($"/api/v1/settings/{user.Id}", new
        {
            autoExecute = true,
            minConfidence = 0.7m,
            negativeLimit = 5000m,
            interestRate = 0.05m,
            watchlist = new[] { "INFY", "HDFCBANK" }
        });
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // GET reflects the update.
        var getResponse = await _client.GetFromJsonAsync<TestEnvelope<SettingsDto>>($"/api/v1/settings/{user.Id}");
        getResponse!.Data!.AutoExecute.Should().BeTrue();
        getResponse.Data.MinConfidence.Should().Be(0.7m);
        getResponse.Data.NegativeLimit.Should().Be(5000m);
        getResponse.Data.Watchlist.Should().BeEquivalentTo("INFY", "HDFCBANK");
    }

    private record SettingsDto(int Id, int UserId, bool AutoExecute, decimal MinConfidence, decimal NegativeLimit, decimal InterestRate, string[] Watchlist);
}
