using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Modules.Portfolio.DTOs;
using VoidCapital.Api.Modules.Portfolio.Models;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Shared.Repositories;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class SettingsController : ControllerBase
{
    private static readonly JsonSerializerOptions WatchlistJson = new(JsonSerializerDefaults.Web);

    private readonly ISettingsRepository _settingsRepo;

    public SettingsController(ISettingsRepository settingsRepo)
    {
        _settingsRepo = settingsRepo;
    }

    [HttpGet("{userId:int}")]
    public async Task<ActionResult<ApiResponse<SettingsDto>>> GetSettings(int userId)
    {
        var settings = await GetOrThrowAsync(userId);
        return Ok(ApiResponse<SettingsDto>.Ok(ToDto(settings)));
    }

    [HttpPut("{userId:int}")]
    public async Task<ActionResult<ApiResponse<SettingsDto>>> UpdateSettings(
        int userId, [FromBody] UpdateSettingsRequest request)
    {
        var settings = await GetOrThrowAsync(userId);

        settings.AutoExecute = request.AutoExecute;
        settings.MinConfidence = request.MinConfidence;
        settings.NegativeLimit = request.NegativeLimit;
        settings.InterestRate = request.InterestRate;
        settings.Watchlist = JsonSerializer.Serialize(request.Watchlist, WatchlistJson);

        await _settingsRepo.UpdateAsync(settings);
        return Ok(ApiResponse<SettingsDto>.Ok(ToDto(settings)));
    }

    private async Task<UserSettings> GetOrThrowAsync(int userId) =>
        await _settingsRepo.GetByUserIdAsync(userId)
        ?? throw new NotFoundException($"Settings for user {userId} were not found.");

    private static SettingsDto ToDto(UserSettings s) => new(
        s.Id,
        s.UserId,
        s.AutoExecute,
        s.MinConfidence,
        s.NegativeLimit,
        s.InterestRate,
        DeserializeWatchlist(s.Watchlist));

    private static string[] DeserializeWatchlist(string json)
    {
        try
        {
            var list = JsonSerializer.Deserialize<string[]>(json, WatchlistJson);
            return list ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
