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

/// <summary>
/// Full-stack signal flow against real PostgreSQL: ingest via HTTP, query
/// today's pending signals, approve/reject, auto-execute into a real trade.
/// Each test creates its own user so the shared test DB stays isolated.
/// </summary>
[Collection("integration")]
public class SignalFlowIntegrationTests : IDisposable
{
    private readonly IntegrationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<int> _createdUsers = new();

    public SignalFlowIntegrationTests(IntegrationFactory factory)
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
            DELETE FROM signals.signal_performance
            WHERE signal_id IN (SELECT id FROM signals.model_predictions WHERE user_id = {0});
            DELETE FROM signals.model_predictions WHERE user_id = {0};
            DELETE FROM portfolio.trade_log WHERE user_id = {0};
            DELETE FROM portfolio.holdings WHERE user_id = {0};
            DELETE FROM portfolio.pnl_snapshots WHERE user_id = {0};
            DELETE FROM identity.settings WHERE user_id = {0};
            DELETE FROM identity.users WHERE id = {0};
            """, userId);
    }

    private async Task<int> CreateUserAsync(decimal cash = 100000m, bool autoExecute = false)
    {
        var email = $"it-{Guid.NewGuid():N}@voidcapital.test";
        var user = new User
        {
            Name = "IT User",
            Email = email,
            StartingBudget = cash,
            CurrentCash = cash,
            CreatedAt = DateTime.UtcNow
        };

        await using var db = await _factory.CreateDbAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        db.UserSettings.Add(new UserSettings
        {
            UserId = user.Id,
            AutoExecute = autoExecute,
            MinConfidence = 0.5m,
            NegativeLimit = 0m,
            InterestRate = 0m,
            Watchlist = "[]"
        });
        await db.SaveChangesAsync();

        _createdUsers.Add(user.Id);
        return user.Id;
    }

    /// <summary>
    /// Seeds a stock price so auto-execute can price the trade. The stocks PK
    /// is (symbol, date); prior rows for today are removed first so the seed
    /// stays idempotent across runs.
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

    private async Task<List<IngestedSignal>> IngestAsync(int userId, params object[] signals)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/admin/ingest-signals", signals);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal[]>>();
        return envelope!.Data!.ToList();
    }

    private record IngestedSignal(int Id, string Symbol, string Action, string Status, int? SuggestedQuantity);

    // ---------- Ingest ----------

    [Fact]
    public async Task Ingest_CreatesPendingSignalWithUserIdAndQuantity()
    {
        var userId = await CreateUserAsync();

        var signals = await IngestAsync(userId, new
        {
            userId,
            symbol = "RELIANCE",
            action = "BUY",
            confidence = 0.75m,
            reason = "SMA crossover bullish",
            modelName = "sma",
            suggestedQuantity = 10,
            entryPrice = 2860m,
            targetPrice = 3000m,
            stopLoss = 2700m
        });

        signals.Should().ContainSingle();
        signals[0].Symbol.Should().Be("RELIANCE");
        signals[0].Action.Should().Be("BUY");
        signals[0].Status.Should().Be("PENDING");
        signals[0].SuggestedQuantity.Should().Be(10);

        // Verify the signal + performance rows were created in the DB.
        await using var db = await _factory.CreateDbAsync();
        var signal = await db.Signals.FindAsync(signals[0].Id);
        signal.Should().NotBeNull();
        signal!.UserId.Should().Be(userId);
        var perf = await db.SignalPerformances
            .FirstOrDefaultAsync(p => p.SignalId == signals[0].Id);
        perf.Should().NotBeNull();
        perf!.EntryPrice.Should().Be(2860m);
        perf.TargetPrice.Should().Be(3000m);
    }

    [Fact]
    public async Task Ingest_WithoutUserId_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/admin/ingest-signals", new[]
        {
            new { symbol = "TCS", action = "BUY", confidence = 0.5m, modelName = "rsi" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Today ----------

    [Fact]
    public async Task Today_ReturnsOnlyPendingSignalsForUser()
    {
        var userId = await CreateUserAsync();
        await IngestAsync(userId, new
        {
            userId, symbol = "RELIANCE", action = "BUY", confidence = 0.8m,
            modelName = "sma", suggestedQuantity = 10
        });

        var response = await _client.GetAsync($"/api/v1/signals/today/{userId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal[]>>();
        envelope!.Data.Should().ContainSingle(s => s.Status == "PENDING");
    }

    // ---------- Manual approve / reject ----------

    [Fact]
    public async Task Approve_WithAutoExecuteOff_MarksApproved()
    {
        var userId = await CreateUserAsync(autoExecute: false);
        var signals = await IngestAsync(userId, new
        {
            userId, symbol = "RELIANCE", action = "BUY", confidence = 0.7m,
            modelName = "sma", suggestedQuantity = 10
        });

        var response = await _client.PostAsync($"/api/v1/signals/{signals[0].Id}/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal>>();
        envelope!.Data.Status.Should().Be("APPROVED");

        // No trade should have been created.
        await using var db = await _factory.CreateDbAsync();
        var trades = await db.Trades.Where(t => t.UserId == userId).ToListAsync();
        trades.Should().BeEmpty();
    }

    [Fact]
    public async Task Reject_MarksRejected()
    {
        var userId = await CreateUserAsync();
        var signals = await IngestAsync(userId, new
        {
            userId, symbol = "RELIANCE", action = "BUY", confidence = 0.7m,
            modelName = "sma", suggestedQuantity = 10
        });

        var response = await _client.PostAsync($"/api/v1/signals/{signals[0].Id}/reject", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal>>();
        envelope!.Data.Status.Should().Be("REJECTED");
    }

    [Fact]
    public async Task Approve_Twice_Returns400()
    {
        var userId = await CreateUserAsync(autoExecute: false);
        var signals = await IngestAsync(userId, new
        {
            userId, symbol = "RELIANCE", action = "BUY", confidence = 0.7m,
            modelName = "sma", suggestedQuantity = 10
        });

        await _client.PostAsync($"/api/v1/signals/{signals[0].Id}/approve", null);
        var second = await _client.PostAsync($"/api/v1/signals/{signals[0].Id}/approve", null);

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ---------- Auto-execute ----------

    [Fact]
    public async Task Approve_WithAutoExecuteOn_ExecutesTradeAndMarksExecuted()
    {
        var userId = await CreateUserAsync(cash: 100000m, autoExecute: true);
        await SeedStockAsync("ITST_RELIANCE", 2860m);
        var signals = await IngestAsync(userId, new
        {
            userId, symbol = "ITST_RELIANCE", action = "BUY", confidence = 0.8m,
            modelName = "sma", suggestedQuantity = 10,
            entryPrice = 2860m, targetPrice = 3000m, stopLoss = 2700m
        });

        var response = await _client.PostAsync($"/api/v1/signals/{signals[0].Id}/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal>>();
        envelope!.Data.Status.Should().Be("EXECUTED");

        await using var db = await _factory.CreateDbAsync();
        var trade = await db.Trades
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.Id)
            .FirstOrDefaultAsync();
        trade.Should().NotBeNull();
        trade!.Symbol.Should().Be("ITST_RELIANCE");
        trade.Quantity.Should().Be(10);
        trade.Type.Should().Be("BUY");
    }

    [Fact]
    public async Task Approve_WithAutoExecuteOnAndInsufficientCash_MarksFailedWithReason()
    {
        var userId = await CreateUserAsync(cash: 1000m, autoExecute: true);
        await SeedStockAsync("ITST_EXPENSIVE", 5000m);
        var signals = await IngestAsync(userId, new
        {
            userId, symbol = "ITST_EXPENSIVE", action = "BUY", confidence = 0.8m,
            modelName = "sma", suggestedQuantity = 10
        });

        var response = await _client.PostAsync($"/api/v1/signals/{signals[0].Id}/approve", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal>>();
        envelope!.Data.Status.Should().Be("FAILED");

        // failure_reason persisted to the model_predictions row.
        await using var db = await _factory.CreateDbAsync();
        var signal = await db.Signals.FindAsync(signals[0].Id);
        signal.Should().NotBeNull();
        signal!.Status.Should().Be(SignalStatus.FAILED);
        signal.FailureReason.Should().Contain("Insufficient cash");
    }

    [Fact]
    public async Task Approve_WithAutoExecuteOnAndNoQuantity_MarksFailed()
    {
        var userId = await CreateUserAsync(cash: 100000m, autoExecute: true);
        var signals = await IngestAsync(userId, new
        {
            userId, symbol = "ITST_NOQTY", action = "BUY", confidence = 0.8m, modelName = "sma"
        });

        var response = await _client.PostAsync($"/api/v1/signals/{signals[0].Id}/approve", null);

        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal>>();
        envelope!.Data.Status.Should().Be("FAILED");
    }

    // ---------- Batch ----------

    [Fact]
    public async Task BatchApprove_ProcessesEachSignal()
    {
        var userId = await CreateUserAsync(autoExecute: false);
        var signals = await IngestAsync(userId,
            new { userId, symbol = "RELIANCE", action = "BUY", confidence = 0.7m, modelName = "sma", suggestedQuantity = 10 },
            new { userId, symbol = "TCS", action = "BUY", confidence = 0.6m, modelName = "rsi", suggestedQuantity = 5 });

        var response = await _client.PostAsJsonAsync("/api/v1/signals/batch-approve",
            new { ids = signals.Select(s => s.Id) });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<BatchResult[]>>();
        envelope!.Data.Should().HaveCount(2);
        envelope.Data.Should().OnlyContain(r => r.Success);
    }

    private record BatchResult(int Id, bool Success, string? Error);

    private record TestEnvelope<T>(bool Success, T? Data, string? Error, string? TraceId);
}
