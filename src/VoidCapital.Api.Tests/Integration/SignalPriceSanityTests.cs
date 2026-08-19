using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.Models;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

[Collection("integration")]
public class SignalPriceSanityTests : IDisposable
{
    private readonly IntegrationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<int> _createdUsers = new();

    public SignalPriceSanityTests(IntegrationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthedClient();
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
            DELETE FROM signals.signal_performance
            WHERE signal_id IN (SELECT id FROM signals.model_predictions WHERE user_id = {0});
            DELETE FROM signals.model_predictions WHERE user_id = {0};
            DELETE FROM portfolio.trade_log WHERE user_id = {0};
            DELETE FROM portfolio.holdings WHERE user_id = {0};
            DELETE FROM portfolio.pnl_snapshots WHERE user_id = {0};
            DELETE FROM portfolio.watchlist WHERE user_id = {0};
            DELETE FROM identity.settings WHERE user_id = {0};
            DELETE FROM identity.users WHERE id = {0};
            """, userId);
    }

    private async Task<int> CreateUserAsync(decimal cash = 100000m, bool autoExecute = false)
    {
        var email = $"it-{Guid.NewGuid():N}@voidcapital.test";
        var user = new User { Name = "IT User", Email = email, StartingBudget = cash, CurrentCash = cash, CreatedAt = DateTime.UtcNow };
        await using var db = await _factory.CreateDbAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserSettings.Add(new UserSettings { UserId = user.Id, AutoExecute = autoExecute, MinConfidence = 0.5m, NegativeLimit = 0m, InterestRate = 0m, Watchlist = "[]" });
        await db.SaveChangesAsync();
        _createdUsers.Add(user.Id);
        return user.Id;
    }

    private async Task SeedStockAsync(string symbol, decimal price)
    {
        await using var db = await _factory.CreateDbAsync();
        await db.StockPrices.Where(s => s.Symbol == symbol && s.Date == DateOnly.FromDateTime(DateTime.UtcNow)).ExecuteDeleteAsync();
        db.StockPrices.Add(new StockPrice(symbol, DateOnly.FromDateTime(DateTime.UtcNow), price, price, price, price, 10000));
        await db.SaveChangesAsync();
    }

    private async Task<List<IngestedSignal>> IngestAsync(int userId, params object[] signals)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/admin/ingest-signals", signals);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal[]>>();
        return envelope!.Data!.ToList();
    }

    private record IngestedSignal(int Id, string Symbol, string Action, string Status, int? SuggestedQuantity);
    private record TestEnvelope<T>(bool Success, T? Data, string? Error, string? TraceId);

    [Fact]
    public async Task Approve_WithAutoExecuteOnAndPriceDeviation_MarksFailed()
    {
        var userId = await CreateUserAsync(cash: 100000m, autoExecute: true);
        // Signal entry price 2860, market price 3200 (11.8% deviation > 10%)
        await SeedStockAsync("ITST_PRICE", 3200m);
        var signals = await IngestAsync(userId, new
        {
            userId, symbol = "ITST_PRICE", action = "BUY", confidence = 0.8m,
            modelName = "sma", suggestedQuantity = 10,
            entryPrice = 2860m
        });

        var response = await _client.PostAsync($"/api/v1/signals/{signals[0].Id}/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal>>();
        envelope!.Data!.Status.Should().Be("FAILED");

        await using var db = await _factory.CreateDbAsync();
        var signal = await db.Signals.FindAsync(signals[0].Id);
        signal.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().Contain("Price deviation");
    }
}
