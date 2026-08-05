using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared.Repositories;
using VoidCapital.Api.Tests.Integration;

namespace VoidCapital.Api.Tests.Repositories;

[Collection("integration")]
public class SettingsRepositoryTests
{
    private readonly IntegrationFactory _factory;

    public SettingsRepositoryTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UpdateAsync_SyncsWatchlistTable()
    {
        var dbFactory = _factory.DbFactory;
        var repo = new SettingsRepository(dbFactory);
        
        // Setup: settings rows reference identity.users (FK_settings_user), so
        // the user must exist before the settings row is inserted.
        var userId = 999;
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = new User
        {
            Name = "IT Settings Repo",
            Email = $"it-settings-repo-{Guid.NewGuid():N}@voidcapital.test",
            StartingBudget = 100000m,
            CurrentCash = 100000m,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        userId = user.Id;
        db.UserSettings.Add(new UserSettings { UserId = userId, Watchlist = "[\"TCS\", \"INFY\"]" });
        await db.SaveChangesAsync();

        // Act
        var settings = await repo.GetByUserIdAsync(userId);
        settings!.Watchlist = "[\"TCS\", \"RELIANCE\"]";
        await repo.UpdateAsync(settings);

        // Assert
        var watchlist = await db.Watchlist.Where(w => w.UserId == userId).Select(w => w.Symbol).ToListAsync();
        Assert.Contains("TCS", watchlist);
        Assert.Contains("RELIANCE", watchlist);
        Assert.DoesNotContain("INFY", watchlist);
    }
}