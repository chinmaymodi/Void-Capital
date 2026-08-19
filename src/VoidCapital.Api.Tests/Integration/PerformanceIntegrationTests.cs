using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// Performance endpoints (models aggregates, resolved-signal paging, portfolio
/// comparison) against real PostgreSQL. Signals are ingested over HTTP, then
/// their linked performance rows are resolved in the DB to produce settled
/// outcomes. Unique model names keep assertions isolated from other tests.
/// </summary>
[Collection("integration")]
public class PerformanceIntegrationTests : IDisposable
{
    private readonly IntegrationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<int> _createdUsers = new();

    public PerformanceIntegrationTests(IntegrationFactory factory)
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

    private async Task<int> CreateUserAsync(string name, decimal cash)
    {
        var user = new User
        {
            Name = name,
            Email = $"it-{Guid.NewGuid():N}@voidcapital.test",
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

    private async Task<int> IngestAsync(int userId, string symbol, string model, decimal confidence)
    {
        var response = await _client.PostAsJsonAsync("/api/v1/admin/ingest-signals", new[]
        {
            new
            {
                userId, symbol, action = "BUY", confidence, modelName = model,
                suggestedQuantity = 10, entryPrice = 100m, targetPrice = 110m, stopLoss = 95m
            }
        });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<IngestedSignal[]>>();
        return envelope!.Data!.Single().Id;
    }

    /// <summary>Marks a signal's performance row as settled with the given outcome.</summary>
    private async Task ResolveAsync(int signalId, string outcome, decimal returnPct, DateTime resolvedAt)
    {
        await using var db = await _factory.CreateDbAsync();
        var perf = await db.SignalPerformances.FirstAsync(p => p.SignalId == signalId);
        perf.Outcome = outcome;
        perf.ExitPrice = 100m * (1 + returnPct);
        perf.ActualReturn = returnPct;
        perf.ResolvedAt = resolvedAt;
        await db.SaveChangesAsync();
    }

    private record IngestedSignal(int Id, string Symbol, string Action, string Status, int? SuggestedQuantity);

    private record ModelPerf(
        string ModelName, int TotalSignals, int ResolvedSignals, int HitTargetCount,
        decimal WinRate, decimal AvgReturn, decimal? BestReturn, decimal? WorstReturn);

    private record ResolvedSignal(
        int SignalId, string Symbol, string Action, string ModelName, decimal EntryPrice,
        decimal? ExitPrice, string Outcome, decimal? ActualReturn, int EvaluationDays);

    private record PagedResolved<T>(List<T> Items, int Total, int Page, int PageSize);

    private record ComparisonPortfolio(
        int UserId, string Name, decimal Cash, decimal HoldingsValue, decimal TotalValue,
        decimal TotalReturn, decimal TotalReturnPercent, decimal StartingBudget);

    private record ComparisonGap(
        string Leader, string Trailer, decimal GapRupees, decimal GapPercent);

    private record PortfolioComparison(
        List<ComparisonPortfolio> Portfolios, List<ComparisonGap> Gaps);

    private record TestEnvelope<T>(bool Success, T? Data, string? Error, string? TraceId);

    // ---------- /performance/models ----------

    [Fact]
    public async Task GetModels_AggregatesResolvedOutcomesPerModel()
    {
        var userId = await CreateUserAsync("IT Perf Models", 100000m);
        var modelName = $"itst_models_{Guid.NewGuid():N}";

        var s1 = await IngestAsync(userId, "RELIANCE", modelName, 0.8m);
        var s2 = await IngestAsync(userId, "TCS", modelName, 0.7m);
        await ResolveAsync(s1, "HIT_TARGET", 0.05m, DateTime.UtcNow.AddMinutes(-5));
        await ResolveAsync(s2, "HIT_STOP", -0.08m, DateTime.UtcNow.AddMinutes(-2));

        var response = await _client.GetAsync("/api/v1/performance/models");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<ModelPerf[]>>();
        var model = envelope!.Data!.Single(m => m.ModelName == modelName);

        model.TotalSignals.Should().Be(2);
        model.ResolvedSignals.Should().Be(2);
        model.HitTargetCount.Should().Be(1);
        model.WinRate.Should().Be(0.5m);
        model.AvgReturn.Should().BeApproximately(-0.015m, 0.0001m);
        model.BestReturn.Should().Be(0.05m);
        model.WorstReturn.Should().Be(-0.08m);
    }

    [Fact]
    public async Task GetModels_PendingSignalsCountButAreNotResolved()
    {
        var userId = await CreateUserAsync("IT Perf Pending", 100000m);
        var modelName = $"itst_pending_{Guid.NewGuid():N}";

        await IngestAsync(userId, "RELIANCE", modelName, 0.6m);

        var envelope = await _client.GetFromJsonAsync<TestEnvelope<ModelPerf[]>>(
            "/api/v1/performance/models");
        var model = envelope!.Data!.Single(m => m.ModelName == modelName);

        model.TotalSignals.Should().Be(1);
        model.ResolvedSignals.Should().Be(0);
        model.WinRate.Should().Be(0m);
        model.BestReturn.Should().BeNull();
        model.WorstReturn.Should().BeNull();
    }

    // ---------- /performance/signals ----------

    [Fact]
    public async Task GetResolvedSignals_FiltersByUserAndModel_NewestFirst()
    {
        var userId = await CreateUserAsync("IT Perf Resolved", 100000m);
        var modelName = $"itst_resolved_{Guid.NewGuid():N}";

        var s1 = await IngestAsync(userId, "RELIANCE", modelName, 0.8m);
        var s2 = await IngestAsync(userId, "TCS", modelName, 0.7m);
        var s3 = await IngestAsync(userId, "INFY", modelName, 0.6m);
        await ResolveAsync(s1, "HIT_TARGET", 0.05m, DateTime.UtcNow.AddMinutes(-10));
        await ResolveAsync(s2, "HIT_STOP", -0.08m, DateTime.UtcNow.AddMinutes(-5));
        await ResolveAsync(s3, "EXPIRED", 0m, DateTime.UtcNow.AddMinutes(-1));

        var response = await _client.GetAsync(
            $"/api/v1/performance/signals?userId={userId}&model={modelName}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<PagedResolved<ResolvedSignal>>>();

        envelope!.Data!.Total.Should().Be(3);
        envelope.Data.Items.Should().HaveCount(3);
        // Newest resolution first.
        envelope.Data.Items[0].SignalId.Should().Be(s3);
        envelope.Data.Items[2].SignalId.Should().Be(s1);
        envelope.Data.Items.Should().OnlyContain(r => r.ModelName == modelName);
        envelope.Data.Items.Should().Contain(r => r.Outcome == "HIT_TARGET");
    }

    [Fact]
    public async Task GetResolvedSignals_ClampsPageSizeTo100()
    {
        var userId = await CreateUserAsync("IT Perf Clamp", 100000m);
        var modelName = $"itst_clamp_{Guid.NewGuid():N}";

        var s1 = await IngestAsync(userId, "RELIANCE", modelName, 0.8m);
        await ResolveAsync(s1, "HIT_TARGET", 0.05m, DateTime.UtcNow);

        var response = await _client.GetAsync(
            $"/api/v1/performance/signals?userId={userId}&pageSize=500");
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<PagedResolved<ResolvedSignal>>>();

        envelope!.Data!.PageSize.Should().Be(100);
        envelope.Data.Total.Should().Be(1);
        envelope.Data.Items.Should().ContainSingle();
    }

    // ---------- /performance/compare ----------

    [Fact]
    public async Task GetCompare_ReturnsPortfoliosAndPairwiseGap()
    {
        var nameA = $"IT Perf Alpha {Guid.NewGuid():N}";
        var nameB = $"IT Perf Beta {Guid.NewGuid():N}";
        var userIdA = await CreateUserAsync(nameA, 100000m);
        var userIdB = await CreateUserAsync(nameB, 100000m);
        // Move cash directly so totals differ deterministically.
        await using var db0 = await _factory.CreateDbAsync();
        var a = await db0.Users.FindAsync(userIdA);
        var b = await db0.Users.FindAsync(userIdB);
        a!.CurrentCash = 90000m;
        b!.CurrentCash = 110000m;
        await db0.SaveChangesAsync();

        var response = await _client.GetAsync("/api/v1/performance/compare");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelope = await response.Content.ReadFromJsonAsync<TestEnvelope<PortfolioComparison>>();

        var portfolioA = envelope!.Data!.Portfolios.Single(p => p.UserId == userIdA);
        portfolioA.TotalValue.Should().Be(90000m);
        portfolioA.TotalReturn.Should().Be(-10000m);
        portfolioA.TotalReturnPercent.Should().Be(-0.1m);
        portfolioA.StartingBudget.Should().Be(100000m);

        var portfolioB = envelope.Data.Portfolios.Single(p => p.UserId == userIdB);
        portfolioB.TotalValue.Should().Be(110000m);
        portfolioB.TotalReturn.Should().Be(10000m);
        portfolioB.TotalReturnPercent.Should().Be(0.1m);
        portfolioB.StartingBudget.Should().Be(100000m);

        var gap = envelope.Data.Gaps.Single(g =>
            (g.Leader == nameB && g.Trailer == nameA) ||
            (g.Leader == nameA && g.Trailer == nameB));
        gap.GapRupees.Should().Be(20000m);
        gap.GapPercent.Should().BeApproximately(20000m / 90000m, 0.0001m);
    }

    [Fact]
    public async Task GetCompare_TiedTotals_NamesLeaderWithZeroGap()
    {
        var nameA = $"IT Perf TieA {Guid.NewGuid():N}";
        var nameB = $"IT Perf TieB {Guid.NewGuid():N}";
        var userIdA = await CreateUserAsync(nameA, 100000m);
        var userIdB = await CreateUserAsync(nameB, 100000m);

        var envelope = await _client.GetFromJsonAsync<TestEnvelope<PortfolioComparison>>(
            "/api/v1/performance/compare");

        var gap = envelope!.Data!.Gaps.Single(g =>
            (g.Leader == nameA && g.Trailer == nameB) ||
            (g.Leader == nameB && g.Trailer == nameA));
        gap.GapRupees.Should().Be(0m);
        // A leader is still named on a tie (first-listed user wins).
        gap.Leader.Should().Be(nameA);
        gap.Trailer.Should().Be(nameB);
    }
}
