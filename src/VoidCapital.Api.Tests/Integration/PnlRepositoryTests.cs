using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// PnlRepository against real PostgreSQL: add, per-user history (ordered),
/// and latest-snapshot lookup.
/// </summary>
[Collection("integration")]
public class PnlRepositoryTests
{
    private readonly IntegrationFactory _factory;

    public PnlRepositoryTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    private PnlRepository CreateRepo() => new(_factory.DbFactory);

    private async Task<int> CreateUserAsync()
    {
        var user = new User
        {
            Name = "IT Pnl",
            Email = $"it-pnl-{Guid.NewGuid():N}@voidcapital.test",
            StartingBudget = 100000m,
            CurrentCash = 100000m,
            CreatedAt = DateTime.UtcNow
        };
        await using var db = await _factory.CreateDbAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private async Task CleanupAsync(int userId)
    {
        await using var db = await _factory.CreateDbAsync();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM portfolio.pnl_snapshots WHERE user_id = {0}; " +
            "DELETE FROM identity.users WHERE id = {0};", userId);
    }

    [Fact]
    public async Task AddAsync_PersistsSnapshot()
    {
        var userId = await CreateUserAsync();
        try
        {
            var repo = CreateRepo();
            var snapshot = new PnlSnapshot
            {
                UserId = userId,
                Date = new DateOnly(2024, 1, 2),
                PortfolioValue = 101000m,
                CashValue = 51000m,
                HoldingsValue = 50000m
            };

            var changes = await repo.AddAsync(snapshot);

            changes.Should().Be(1);
            snapshot.Id.Should().BeGreaterThan(0);

            await using var db = await _factory.CreateDbAsync();
            var reloaded = await db.PnlSnapshots.SingleAsync(p => p.Id == snapshot.Id);
            reloaded.PortfolioValue.Should().Be(101000m);
            reloaded.CashValue.Should().Be(51000m);
            reloaded.HoldingsValue.Should().Be(50000m);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task GetByUserIdAsync_FiltersAndOrdersByDate()
    {
        var userId = await CreateUserAsync();
        try
        {
            var repo = CreateRepo();
            await repo.AddAsync(new PnlSnapshot
            {
                UserId = userId, Date = new DateOnly(2024, 1, 10),
                PortfolioValue = 102000m, CashValue = 52000m, HoldingsValue = 50000m
            });
            await repo.AddAsync(new PnlSnapshot
            {
                UserId = userId, Date = new DateOnly(2024, 1, 2),
                PortfolioValue = 101000m, CashValue = 51000m, HoldingsValue = 50000m
            });

            var snapshots = (await repo.GetByUserIdAsync(userId)).ToList();

            snapshots.Should().HaveCount(2);
            snapshots[0].Date.Should().Be(new DateOnly(2024, 1, 2));
            snapshots[1].Date.Should().Be(new DateOnly(2024, 1, 10));
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task GetLatestAsync_ReturnsNewestSnapshot()
    {
        var userId = await CreateUserAsync();
        try
        {
            var repo = CreateRepo();
            await repo.AddAsync(new PnlSnapshot
            {
                UserId = userId, Date = new DateOnly(2024, 1, 2),
                PortfolioValue = 101000m, CashValue = 51000m, HoldingsValue = 50000m
            });
            await repo.AddAsync(new PnlSnapshot
            {
                UserId = userId, Date = new DateOnly(2024, 1, 10),
                PortfolioValue = 102000m, CashValue = 52000m, HoldingsValue = 50000m
            });

            var latest = await repo.GetLatestAsync(userId);

            latest.Should().NotBeNull();
            latest!.Date.Should().Be(new DateOnly(2024, 1, 10));
            latest.PortfolioValue.Should().Be(102000m);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task GetLatestAsync_NoSnapshots_ReturnsNull()
    {
        var userId = await CreateUserAsync();
        try
        {
            var latest = await CreateRepo().GetLatestAsync(userId);
            latest.Should().BeNull();
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }
}