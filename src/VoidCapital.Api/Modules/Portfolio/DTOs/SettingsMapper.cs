using System.Text.Json;
using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Modules.Portfolio.DTOs;

/// <summary>
/// Single source of truth for UserSettings &lt;-&gt; SettingsDto mapping and
/// watchlist JSON (de)serialization. Shared by SettingsController (user page)
/// and AdminController (system-user configuration).
/// </summary>
public static class SettingsMapper
{
    private static readonly JsonSerializerOptions WatchlistJson = new(JsonSerializerDefaults.Web);

    public static SettingsDto ToDto(UserSettings s) => new(
        s.Id,
        s.UserId,
        s.AutoExecute,
        s.IsHalted,
        s.MinConfidence,
        s.NegativeLimit,
        s.InterestRate,
        DeserializeWatchlist(s.Watchlist));

    public static string SerializeWatchlist(string[] watchlist) =>
        JsonSerializer.Serialize(watchlist, WatchlistJson);

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
