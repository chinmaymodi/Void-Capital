using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

public class SettingsControllerTests
{
    private readonly Mock<ISettingsRepository> _settingsRepo = new();
    private SettingsController CreateController() => new SettingsController(_settingsRepo.Object).AsAdmin();

    private static UserSettings MakeSettings() => new()
    {
        Id = 1,
        UserId = 1,
        AutoExecute = false,
        MinConfidence = 0.5m,
        NegativeLimit = 0m,
        InterestRate = 0m,
        Watchlist = "[\"RELIANCE\",\"TCS\"]"
    };

    [Fact]
    public async Task GetSettings_ReturnsDtoWithWatchlistDeserialized()
    {
        _settingsRepo.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(MakeSettings());

        var result = await CreateController().GetSettings(1);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SettingsDto>>().Subject;
        envelope.Success.Should().BeTrue();
        envelope.Data!.UserId.Should().Be(1);
        envelope.Data.AutoExecute.Should().BeFalse();
        envelope.Data.Watchlist.Should().BeEquivalentTo("RELIANCE", "TCS");
    }

    [Fact]
    public async Task GetSettings_WhenMissing_ThrowsNotFound()
    {
        _settingsRepo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync((UserSettings?)null);

        var act = () => CreateController().GetSettings(99);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task UpdateSettings_PersistsRequestAndReturnsDto()
    {
        var existing = MakeSettings();
        _settingsRepo.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(existing);

        var request = new UpdateSettingsRequest(
            AutoExecute: true,
            MinConfidence: 0.7m,
            NegativeLimit: 5000m,
            InterestRate: 0.05m,
            Watchlist: new[] { "INFY", "HDFCBANK" });

        var result = await CreateController().UpdateSettings(1, request);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SettingsDto>>().Subject;
        envelope.Data!.AutoExecute.Should().BeTrue();
        envelope.Data.MinConfidence.Should().Be(0.7m);
        envelope.Data.NegativeLimit.Should().Be(5000m);
        envelope.Data.Watchlist.Should().BeEquivalentTo("INFY", "HDFCBANK");

        // Entity was updated before persistence: watchlist serialized to JSON.
        existing.Watchlist.Should().Contain("INFY");
        _settingsRepo.Verify(r => r.UpdateAsync(existing), Times.Once);
    }

    [Fact]
    public async Task UpdateSettings_WhenMissing_ThrowsNotFound()
    {
        _settingsRepo.Setup(r => r.GetByUserIdAsync(99)).ReturnsAsync((UserSettings?)null);

        var act = () => CreateController().UpdateSettings(99, new UpdateSettingsRequest(
            false, 0.5m, 0m, 0m, Array.Empty<string>()));

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task GetSettings_WhenWatchlistIsInvalidJson_ReturnsEmptyArray()
    {
        var settings = MakeSettings();
        settings.Watchlist = "not-json{";
        _settingsRepo.Setup(r => r.GetByUserIdAsync(1)).ReturnsAsync(settings);

        var result = await CreateController().GetSettings(1);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<SettingsDto>>().Subject;
        envelope.Data!.Watchlist.Should().BeEmpty();
    }
}
