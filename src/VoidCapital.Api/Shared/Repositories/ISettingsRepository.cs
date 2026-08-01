using VoidCapital.Api.Modules.Portfolio.Models;

namespace VoidCapital.Api.Shared.Repositories;

public interface ISettingsRepository
{
    Task<UserSettings?> GetByUserIdAsync(int userId);
    Task UpdateAsync(UserSettings settings);
}
