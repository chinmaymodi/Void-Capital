using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Modules.Signals;
using VoidCapital.Api.Modules.Signals.Models;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// Verifies migrations 009/010 against a fresh migrated Testcontainers DB:
/// reckless agents (users 3, 5, 7) must carry the corrected annual interest
/// rate (18.25% = 0.05% daily, F21), and signal_performance.actual_return
/// must accept returns above 100% (F22, widened to decimal(8,4)).
/// </summary>
[Collection("integration")]
public class MigrationFixIntegrationTests
{
    private readonly IntegrationFactory _factory;

    public MigrationFixIntegrationTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RecklessAgents_SeededWithAnnualInterestRate()
    {
        await using var db = await _factory.CreateDbAsync();

        var rates = await db.UserSettings
            .Where(s => new[] { 3, 5, 7 }.Contains(s.UserId))
            .Select(s => new { s.UserId, s.InterestRate })
            .ToListAsync();

        // All three reckless agents must carry 18.25% annual (0.05% daily).
        rates.Should().HaveCount(3);
        rates.Should().OnlyContain(r => r.InterestRate == 0.1825m);
    }

    [Fact]
    public async Task ActualReturn_AcceptsReturnsAboveOneHundredPercent()
    {
        await using var db = await _factory.CreateDbAsync();

        var user = new User
        {
            Name = "IT Migrations",
            Email = $"it-migrations-{Guid.NewGuid():N}@voidcapital.test",
            StartingBudget = 100000m,
            CurrentCash = 100000m,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var signal = new Signal
        {
            UserId = user.Id,
            Date = DateOnly.FromDateTime(DateTime.UtcNow),
            Symbol = "RELIANCE",
            ModelName = "avg3",
            Action = "BUY",
            Confidence = 0.7m,
            Status = SignalStatus.EXECUTED
        };
        db.Signals.Add(signal);
        await db.SaveChangesAsync();

        // 4.0 = 400% return: overflows decimal(6,4) (max 99.9999), must
        // round-trip through the widened decimal(8,4) column.
        var performance = new SignalPerformance
        {
            SignalId = signal.Id,
            EntryPrice = 100m,
            ExitPrice = 500m,
            Outcome = "HIT_TARGET",
            ActualReturn = 4.0m,
            EvaluationDays = 5,
            CreatedAt = DateTime.UtcNow,
            ResolvedAt = DateTime.UtcNow
        };
        db.SignalPerformances.Add(performance);
        await db.SaveChangesAsync();

        var stored = await db.SignalPerformances
            .AsNoTracking()
            .SingleAsync(p => p.Id == performance.Id);

        stored.ActualReturn.Should().Be(4.0m);
    }
}