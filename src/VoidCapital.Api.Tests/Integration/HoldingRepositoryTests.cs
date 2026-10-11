using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// HoldingRepository against real PostgreSQL: per-user listing (ordered),
/// exact-match lookups (equity + full contract key), add/update/delete.
/// </summary>
[Collection("integration")]
public class HoldingRepositoryTests
{
    private readonly IntegrationFactory _factory;

    public HoldingRepositoryTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    private HoldingRepository CreateRepo() => new(_factory.DbFactory);

    private async Task<int> CreateUserAsync()
    {
        var user = new User
        {
            Name = "IT Holdings",
            Email = $"it-holdings-{Guid.NewGuid():N}@voidcapital.test",
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
            "DELETE FROM portfolio.holdings WHERE user_id = {0}; " +
            "DELETE FROM identity.users WHERE id = {0};", userId);
    }

    [Fact]
    public async Task GetByUserIdAsync_ReturnsOrderedHoldingsForUser()
    {
        var userId = await CreateUserAsync();
        try
        {
            var repo = CreateRepo();
            await repo.AddAsync(new Holding { UserId = userId, Symbol = "TCS", Quantity = 5, AvgPrice = 200m });
            await repo.AddAsync(new Holding { UserId = userId, Symbol = "RELIANCE", Quantity = 10, AvgPrice = 100m });

            var holdings = (await repo.GetByUserIdAsync(userId)).ToList();

            holdings.Should().HaveCount(2);
            holdings[0].Symbol.Should().Be("RELIANCE"); // ordered by symbol
            holdings[1].Symbol.Should().Be("TCS");
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task GetAsync_MatchesUserIdAndSymbol()
    {
        var userId = await CreateUserAsync();
        try
        {
            var repo = CreateRepo();
            var holding = new Holding { UserId = userId, Symbol = "RELIANCE", Quantity = 10, AvgPrice = 100m };
            await repo.AddAsync(holding);

            var found = await repo.GetAsync(userId, "RELIANCE");
            var otherUser = await repo.GetAsync(userId + 999, "RELIANCE");

            found.Should().NotBeNull();
            found!.Id.Should().Be(holding.Id);
            otherUser.Should().BeNull();
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task GetByInstrumentAsync_MatchesFullContractKey()
    {
        var userId = await CreateUserAsync();
        try
        {
            var repo = CreateRepo();
            var expiry = new DateOnly(2026, 10, 29);
            var holding = new Holding
            {
                UserId = userId, InstrumentType = "CE", Symbol = "RELIANCE",
                Expiry = expiry, Strike = 2800m, Quantity = 75, AvgPrice = 45m
            };
            await repo.AddAsync(holding);

            var found = await repo.GetByInstrumentAsync(userId, "CE", "RELIANCE", expiry, 2800m);
            var wrongStrike = await repo.GetByInstrumentAsync(userId, "CE", "RELIANCE", expiry, 2850m);
            var wrongType = await repo.GetByInstrumentAsync(userId, "PE", "RELIANCE", expiry, 2800m);

            found.Should().NotBeNull();
            found!.Id.Should().Be(holding.Id);
            wrongStrike.Should().BeNull();
            wrongType.Should().BeNull();
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        var userId = await CreateUserAsync();
        try
        {
            var repo = CreateRepo();
            var holding = new Holding { UserId = userId, Symbol = "RELIANCE", Quantity = 10, AvgPrice = 100m };
            await repo.AddAsync(holding);

            holding.Quantity = 20;
            holding.AvgPrice = 150m;
            var changes = await repo.UpdateAsync(holding);

            changes.Should().Be(1);
            await using var db = await _factory.CreateDbAsync();
            var reloaded = await db.Holdings.SingleAsync(h => h.Id == holding.Id);
            reloaded.Quantity.Should().Be(20);
            reloaded.AvgPrice.Should().Be(150m);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesHolding()
    {
        var userId = await CreateUserAsync();
        try
        {
            var repo = CreateRepo();
            var holding = new Holding { UserId = userId, Symbol = "RELIANCE", Quantity = 10, AvgPrice = 100m };
            await repo.AddAsync(holding);

            var deleted = await repo.DeleteAsync(holding.Id);
            var missing = await repo.DeleteAsync(holding.Id);

            deleted.Should().Be(1);
            missing.Should().Be(0);
            await using var db = await _factory.CreateDbAsync();
            var count = await db.Holdings.CountAsync(h => h.Id == holding.Id);
            count.Should().Be(0);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }
}