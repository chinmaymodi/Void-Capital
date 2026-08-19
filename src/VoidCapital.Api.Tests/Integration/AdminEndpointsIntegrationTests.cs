using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.MarketData;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals.DTOs;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// Admin endpoints against real PostgreSQL: per-user settings round-trip,
/// global settings propagation, square-off liquidation, status report, and
/// the run-signals integration facade. Each test creates its own user(s)
/// for isolation.
/// </summary>
[Collection("integration")]
public class AdminEndpointsIntegrationTests : IDisposable
{
    private readonly IntegrationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<int> _createdUsers = new();

    public AdminEndpointsIntegrationTests(IntegrationFactory factory)
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

    private async Task<int> CreateUserAsync(string name, decimal cash = 100000m)
    {
        var user = new User
        {
            Name = name,
            Email = $"it-admin-{Guid.NewGuid():N}@voidcapital.test",
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

    /// <summary>Seeds a stock price for today so square-off can price the sell.</summary>
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

    private record SettingsDto(int Id, int UserId, bool AutoExecute, decimal MinConfidence, decimal NegativeLimit, decimal InterestRate, string[] Watchlist);

    private record UserBalance(int UserId, string Name, decimal CurrentCash, decimal TotalValue, decimal TotalReturn, decimal TotalReturnPercent);
    private record AdminStatus(DateTime UtcNow, int PendingSignalCount, UserBalance[] Users);
    private record SquareOffResult(int UserId, int PositionsSold, decimal Proceeds, decimal RemainingCash);

    private record TestEnvelope<T>(bool Success, T? Data, string? Error, string? TraceId);

    // ---------- Per-user settings ----------

    [Fact]
    public async Task AdminSettings_GetReturnsRow_PutRoundTrips()
    {
        var userId = await CreateUserAsync("IT Admin Settings");

        var get = await _client.GetFromJsonAsync<TestEnvelope<SettingsDto>>($"/api/v1/admin/settings/{userId}");
        get!.Data!.MinConfidence.Should().Be(0.5m);
        get.Data.NegativeLimit.Should().Be(0m);

        var put = await _client.PutAsJsonAsync($"/api/v1/admin/settings/{userId}", new
        {
            autoExecute = true,
            minConfidence = 0.8m,
            negativeLimit = 10000m,
            interestRate = 0.07m,
            watchlist = new[] { "TCS", "HDFCBANK" }
        });
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var after = await _client.GetFromJsonAsync<TestEnvelope<SettingsDto>>($"/api/v1/admin/settings/{userId}");
        after!.Data!.AutoExecute.Should().BeTrue();
        after.Data.MinConfidence.Should().Be(0.8m);
        after.Data.NegativeLimit.Should().Be(10000m);
        after.Data.InterestRate.Should().Be(0.07m);
        after.Data.Watchlist.Should().BeEquivalentTo("TCS", "HDFCBANK");
    }

    [Fact]
    public async Task AdminSettings_MissingUser_Returns404()
    {
        var response = await _client.GetAsync("/api/v1/admin/settings/999999");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<object>>();
        envelope!.Success.Should().BeFalse();
    }

    // ---------- Global settings ----------

    [Fact]
    public async Task GlobalSettings_AppliesToEveryUser()
    {
        var userA = await CreateUserAsync("IT Admin Global A");
        var userB = await CreateUserAsync("IT Admin Global B");

        var response = await _client.PutAsJsonAsync("/api/v1/admin/settings/global", new
        {
            minConfidence = 0.9m,
            negativeLimit = 0.0m,
            interestRate = 0.1825m,
            watchlist = new[] { "INFY", "RELIANCE" }
        });

        var a = await _client.GetFromJsonAsync<TestEnvelope<SettingsDto>>($"/api/v1/admin/settings/{userA}");
        a!.Data!.MinConfidence.Should().Be(0.9m);
        a.Data.Watchlist.Should().BeEquivalentTo("INFY", "RELIANCE");

        var b = await _client.GetFromJsonAsync<TestEnvelope<SettingsDto>>($"/api/v1/admin/settings/{userB}");
        b!.Data!.MinConfidence.Should().Be(0.9m);
        b.Data.Watchlist.Should().BeEquivalentTo("INFY", "RELIANCE");
    }

    [Fact]
    public async Task GlobalSettings_SyncsPortfolioWatchlistTable()
    {
        var userA = await CreateUserAsync("IT Admin Global WL A");
        var userB = await CreateUserAsync("IT Admin Global WL B");

        // Global PUT must write portfolio.watchlist rows for every user,
        // not just the settings JSON column. This is the D2 regression:
        // the deployed service binary predates the sync + migration 004.
        var response = await _client.PutAsJsonAsync("/api/v1/admin/settings/global", new
        {
            minConfidence = 0.9m,
            negativeLimit = 0.0m,
            interestRate = 0.1825m,
            watchlist = new[] { "INFY", "RELIANCE" }
        });
        await using var db = await _factory.CreateDbAsync();
        var a = await db.Watchlist.Where(w => w.UserId == userA).Select(w => w.Symbol).ToListAsync();
        var b = await db.Watchlist.Where(w => w.UserId == userB).Select(w => w.Symbol).ToListAsync();
        a.Should().BeEquivalentTo("INFY", "RELIANCE");
        b.Should().BeEquivalentTo("INFY", "RELIANCE");

        // Re-PUT with a different watchlist: old symbols removed, new added.
        var second = await _client.PutAsJsonAsync("/api/v1/admin/settings/global", new
        {
            minConfidence = 0.9m,
            negativeLimit = 0.0m,
            interestRate = 0.1825m,
            watchlist = new[] { "TCS" }
        });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        await using var db2 = await _factory.CreateDbAsync();
        var a2 = await db2.Watchlist.Where(w => w.UserId == userA).Select(w => w.Symbol).ToListAsync();
        a2.Should().BeEquivalentTo("TCS");
    }

    // ---------- Square off ----------

    [Fact]
    public async Task SquareOff_SellsAllHoldings_AndReturnsProceeds()
    {
        var userId = await CreateUserAsync("IT Admin SquareOff", cash: 100000m);
        await SeedStockAsync("ITST_SQ", 1000m);
        var buy = await _client.PostAsJsonAsync($"/api/v1/holdings/{userId}/buy",
            new { symbol = "ITST_SQ", shares = 10 }); // cost 10000, cash -> 90000
        buy.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.PostAsync($"/api/v1/admin/square-off/{userId}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<SquareOffResult>>();

        envelope!.Data!.PositionsSold.Should().Be(1);
        envelope.Data.Proceeds.Should().Be(10000m);
        // 100000 - 10000 (buy) - 13.09 (buy commission) + 10000 (sell) - 11.59 (sell commission) = 99975.32
        envelope.Data.RemainingCash.Should().Be(99975.32m);

        // Holdings table is empty for the user.
        await using var db = await _factory.CreateDbAsync();
        var holdings = await db.Holdings.Where(h => h.UserId == userId).ToListAsync();
        holdings.Should().BeEmpty();

        // Trade log has the SELL entry.
        var sell = await db.Trades.FirstOrDefaultAsync(t => t.UserId == userId && t.Type == "SELL");
        sell.Should().NotBeNull();
        sell!.Symbol.Should().Be("ITST_SQ");
        sell.Quantity.Should().Be(10);
    }

    [Fact]
    public async Task SquareOff_NoHoldings_ReturnsZeroPositions()
    {
        var userId = await CreateUserAsync("IT Admin Empty");

        var response = await _client.PostAsync($"/api/v1/admin/square-off/{userId}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<SquareOffResult>>();

        envelope!.Data!.PositionsSold.Should().Be(0);
        envelope.Data.Proceeds.Should().Be(0m);
        envelope.Data.RemainingCash.Should().Be(100000m);
    }

    [Fact]
    public async Task SquareOff_MissingUser_Returns404()
    {
        var response = await _client.PostAsync("/api/v1/admin/square-off/999999", null);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- Status ----------

    [Fact]
    public async Task Status_ReportsPendingCountAndUserBalances()
    {
        var userId = await CreateUserAsync("IT Admin Status", cash: 50000m);

        // Seed one pending signal so the pending count is at least one.
        var ingest = await _client.PostAsJsonAsync("/api/v1/admin/ingest-signals", new[]
        {
            new { userId, symbol = "RELIANCE", action = "BUY", confidence = 0.7m, modelName = "sma", suggestedQuantity = 10 }
        });
        ingest.StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await _client.GetAsync("/api/v1/admin/status");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<AdminStatus>>();

        envelope!.Data!.PendingSignalCount.Should().BeGreaterThanOrEqualTo(1);
        var balance = envelope.Data.Users.Single(u => u.UserId == userId);
        balance.CurrentCash.Should().Be(50000m);
        balance.TotalValue.Should().Be(50000m);
        balance.TotalReturn.Should().Be(0m);
    }

    // ---------- Run signals (async job) ----------

    [Fact]
    public async Task RunSignals_StartsJobAndCompletes()
    {
        var response = await _client.PostAsync("/api/v1/admin/run-signals", null);
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<SignalJobDto>>();
        envelope!.Data!.Status.Should().Be("RUNNING");
        var jobId = envelope.Data.JobId;

        // Poll until the background job leaves RUNNING (empty settings -> fast).
        SignalJobDto? job = null;
        for (var i = 0; i < 20; i++)
        {
            var status = await _client.GetAsync($"/api/v1/admin/run-signals/{jobId}");
            status.StatusCode.Should().Be(HttpStatusCode.OK);
            var statusEnvelope = await status.Content.ReadFromJsonAsync<TestEnvelope<SignalJobDto>>();
            job = statusEnvelope!.Data!;
            if (job.Status != "RUNNING") break;
            await Task.Delay(250);
        }

        job!.Status.Should().Be("SUCCEEDED");
        job.Message.Should().Contain("0 failures");
    }
}
