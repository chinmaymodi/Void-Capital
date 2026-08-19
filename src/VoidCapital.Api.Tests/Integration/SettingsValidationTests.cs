using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VoidCapital.Api.Modules.Portfolio.Models;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

[Collection("integration")]
public class SettingsValidationTests : IDisposable
{
    private readonly IntegrationFactory _factory;
    private readonly HttpClient _client;
    private readonly List<int> _createdUsers = new();

    public SettingsValidationTests(IntegrationFactory factory)
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
            DELETE FROM identity.settings WHERE user_id = {0};
            DELETE FROM identity.users WHERE id = {0};
            """, userId);
    }

    private async Task<int> CreateUserAsync(string name)
    {
        var user = new User { Name = name, Email = $"it-settings-{Guid.NewGuid():N}@voidcapital.test", StartingBudget = 100000m, CurrentCash = 100000m, CreatedAt = DateTime.UtcNow };
        await using var db = await _factory.CreateDbAsync();
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.UserSettings.Add(new UserSettings { UserId = user.Id, AutoExecute = false, MinConfidence = 0.5m, NegativeLimit = 0m, InterestRate = 0m, Watchlist = "[]" });
        await db.SaveChangesAsync();
        _createdUsers.Add(user.Id);
        return user.Id;
    }

    [Theory]
    [InlineData(1.1, 0, 0)] // MinConfidence > 1
    [InlineData(-0.1, 0, 0)] // MinConfidence < 0
    [InlineData(0.5, -1, 0)] // NegativeLimit < 0
    [InlineData(0.5, 0, 0.6)] // InterestRate > 0.5
    [InlineData(0.5, 0, -0.1)] // InterestRate < 0
    public async Task UpdateSettings_InvalidValues_Returns400(decimal minConfidence, decimal negativeLimit, decimal interestRate)
    {
        var userId = await CreateUserAsync("IT Settings Invalid");

        var response = await _client.PutAsJsonAsync($"/api/v1/admin/settings/{userId}", new
        {
            autoExecute = true,
            minConfidence,
            negativeLimit,
            interestRate,
            watchlist = new string[] { }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(1.1, 0, 0)] // MinConfidence > 1
    [InlineData(-0.1, 0, 0)] // MinConfidence < 0
    [InlineData(0.5, -1, 0)] // NegativeLimit < 0
    [InlineData(0.5, 0, 0.6)] // InterestRate > 0.5
    [InlineData(0.5, 0, -0.1)] // InterestRate < 0
    public async Task UpdateGlobalSettings_InvalidValues_Returns400(decimal minConfidence, decimal negativeLimit, decimal interestRate)
    {
        var response = await _client.PutAsJsonAsync("/api/v1/admin/settings/global", new
        {
            minConfidence,
            negativeLimit,
            interestRate,
            watchlist = new string[] { }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
