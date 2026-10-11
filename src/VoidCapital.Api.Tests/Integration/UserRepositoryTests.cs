using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// UserRepository against real PostgreSQL: find, list, absolute cash update,
/// and atomic delta update.
/// </summary>
[Collection("integration")]
public class UserRepositoryTests
{
    private readonly IntegrationFactory _factory;

    public UserRepositoryTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    private UserRepository CreateRepo() => new(_factory.DbFactory);

    private async Task<int> CreateUserAsync(decimal cash = 100000m)
    {
        var user = new User
        {
            Name = "IT Users",
            Email = $"it-users-{Guid.NewGuid():N}@voidcapital.test",
            StartingBudget = cash,
            CurrentCash = cash,
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
            "DELETE FROM portfolio.trade_log WHERE user_id = {0}; " +
            "DELETE FROM portfolio.pnl_snapshots WHERE user_id = {0}; " +
            "DELETE FROM portfolio.watchlist WHERE user_id = {0}; " +
            "DELETE FROM identity.settings WHERE user_id = {0}; " +
            "DELETE FROM identity.users WHERE id = {0};", userId);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsUser()
    {
        var userId = await CreateUserAsync();
        try
        {
            var user = await CreateRepo().GetByIdAsync(userId);

            user.Should().NotBeNull();
            user!.Id.Should().Be(userId);
            user.CurrentCash.Should().Be(100000m);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task GetByIdAsync_Missing_ReturnsNull()
    {
        var user = await CreateRepo().GetByIdAsync(999999);
        user.Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsUsersOrderedById()
    {
        var userId = await CreateUserAsync();
        try
        {
            var users = (await CreateRepo().GetAllAsync()).ToList();

            users.Should().Contain(u => u.Id == userId);
            // Ordered by id ascending.
            users.Should().BeInAscendingOrder(u => u.Id);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task UpdateCashAsync_SetsAbsoluteValue()
    {
        var userId = await CreateUserAsync(cash: 100000m);
        try
        {
            var changes = await CreateRepo().UpdateCashAsync(userId, 50000m);

            changes.Should().Be(1);
            await using var db = await _factory.CreateDbAsync();
            var reloaded = await db.Users.SingleAsync(u => u.Id == userId);
            reloaded.CurrentCash.Should().Be(50000m);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }

    [Fact]
    public async Task UpdateCashAtomicAsync_AppliesDelta()
    {
        var userId = await CreateUserAsync(cash: 100000m);
        try
        {
            var changes = await CreateRepo().UpdateCashAtomicAsync(userId, -6006.55m);

            changes.Should().Be(1);
            await using var db = await _factory.CreateDbAsync();
            var reloaded = await db.Users.SingleAsync(u => u.Id == userId);
            reloaded.CurrentCash.Should().Be(93993.45m);
        }
        finally
        {
            await CleanupAsync(userId);
        }
    }
}