using System.Text.Json;

namespace VoidCapital.Api.Modules.Auth.Models;

/// <summary>
/// API keys loaded from auth.keys.json (gitignored, generated once). The file
/// holds an "admin" key plus one key per user id ("1".."7"). Keys are random
/// hex strings; the file is copied to the output directory so both dev
/// (dotnet run) and the published Windows service find it next to the DLL.
/// </summary>
public class AuthKeys
{
    public string Admin { get; set; } = string.Empty;

    /// <summary>User id (string) -> API key.</summary>
    public Dictionary<string, string> Users { get; set; } = new();

    public static AuthKeys Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Auth keys file not found at '{path}'. Create auth.keys.json " +
                "with an 'admin' key and one key per user id, then rebuild.");
        }

        var keys = JsonSerializer.Deserialize<AuthKeys>(
                File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException(
                $"Auth keys file '{path}' is empty or invalid JSON.");

        if (string.IsNullOrWhiteSpace(keys.Admin))
        {
            throw new InvalidOperationException(
                $"Auth keys file '{path}' is missing the 'admin' key.");
        }

        return keys;
    }
}