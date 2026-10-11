using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.Models;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// SignalPerformanceRepository against real PostgreSQL: pending lookup with
/// the linked signal loaded, add, and the attach-and-copy update path
/// (including the missing-row no-op).
/// </summary>
[Collection("integration")]
public class SignalPerformanceRepositoryTests
{
    private readonly IntegrationFactory _factory;

    public SignalPerformanceRepositoryTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    private SignalPerformanceRepository CreateRepo() => new(_factory.DbFactory);

    private async Task<int> CreateUserAsync()
    {
        var user = new User
        {
            Name = "IT SigPerf",
            Email = $"it-sigperf-{Guid.NewGuid():N}@voidcapital.test",
            StartingBudget = 100000m,
            CurrentCash = 100000m,
            CreatedAt = DateTime.UtcNow
        };
        await using var db = await _factory.CreateDbAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<Signal> CreateSignalAsync(int userId, string symbol)
    {
        var signal = new Signal
        {
            UserId = userId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            InstrumentType = "EQ",
            Symbol = symbol,
            ModelName = "Ensemble",
            Action = "BUY",
            Confidence = 0.7m,
            Reason = "test",
            SuggestedQuantity = 10,
            Status = SignalStatus.PENDING,
        };
        await using var db = await _factory.CreateDbAsync();
        db.Signals.Add(signal);
        await db.SaveChangesAsync();
        return signal;
    }

    private async Task CleanupAsync(int userId)
    {
        await using var db = await _factory.CreateDbAsync();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM signals.signal_performance " +
            "WHERE signal_id IN (SELECT id FROM signals.model_predictions WHERE user_id = {0}); " +
            "DELETE FROM signals.model_predictions WHERE user_id = {0}; " +
            "DELETE FROM identity.users WHERE id = {0};", userId);
    }

    [Fact]
    public async Task GetPendingPerformances_ReturnsPendingWithSignalLoaded()
    {
        var userId = await CreateUserAsync();
        try
        {
            var signal = await CreateSignalAsync(userId, "SPR1");
            var repo = CreateRepo();
            var perf = new SignalPerformance
            {
                SignalId = signal.Id,
                EntryPrice = 100m,
                Outcome = "PENDING",
                EvaluationDays = 5,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(perf);

            var pending = (await repo.GetPendingPerformancesAsync()).ToList();

            var match = pending.Single(p => p.Id == perf.Id);
            match.Signal.Should().NotBeNull();
            match.Signal!.Symbol.Should().Be("SPR1");
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task GetPendingPerformances_ExcludesResolved()
    {
        var userId = await CreateUserAsync();
        try
        {
            var signal = await CreateSignalAsync(userId, "SPR2");
            var repo = CreateRepo();
            var resolved = new SignalPerformance
            {
                SignalId = signal.Id,
                EntryPrice = 100m,
                Outcome = "HIT_TARGET",
                ExitPrice = 110m,
                ActualReturn = 0.10m,
                ResolvedAt = DateTime.UtcNow,
                EvaluationDays = 5,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(resolved);

            var pending = (await repo.GetPendingPerformancesAsync()).ToList();

            pending.Should().NotContain(p => p.Id == resolved.Id);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task UpdateAsync_CopiesMutableFieldsOnly()
    {
        var userId = await CreateUserAsync();
        try
        {
            var signal = await CreateSignalAsync(userId, "SPR3");
            var repo = CreateRepo();
            var perf = new SignalPerformance
            {
                SignalId = signal.Id,
                EntryPrice = 100m,
                Outcome = "PENDING",
                EvaluationDays = 5,
                CreatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(perf);

            perf.Outcome = "HIT_TARGET";
            perf.ExitPrice = 108m;
            perf.ActualReturn = 0.08m;
            perf.ResolvedAt = DateTime.UtcNow;
            await repo.UpdateAsync(perf);

            await using var db = await _factory.CreateDbAsync();
            var reloaded = await db.SignalPerformances
                .Include(p => p.Signal)
                .SingleAsync(p => p.Id == perf.Id);
            reloaded.Outcome.Should().Be("HIT_TARGET");
            reloaded.ExitPrice.Should().Be(108m);
            reloaded.ActualReturn.Should().Be(0.08m);
            reloaded.ResolvedAt.Should().NotBeNull();
            // The linked signal was not clobbered by the update.
            reloaded.Signal.Should().NotBeNull();
            reloaded.Signal!.Symbol.Should().Be("SPR3");
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task UpdateAsync_MissingRow_IsNoOp()
    {
        var repo = CreateRepo();
        var ghost = new SignalPerformance { Id = 999999, Outcome = "EXPIRED" };

        await repo.UpdateAsync(ghost);

        // No exception; nothing to update.
        await using var db = await _factory.CreateDbAsync();
        var count = await db.SignalPerformances.CountAsync(p => p.Id == 999999);
        count.Should().Be(0);
    }
}