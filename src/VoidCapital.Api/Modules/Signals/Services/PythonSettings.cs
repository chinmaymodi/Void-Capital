namespace VoidCapital.Api.Modules.Signals.Services;

/// <summary>
/// Strongly-typed config for the Python subprocess bridge (Options pattern).
/// Bound from the "Python" section of appsettings.json / appsettings.*.json.
/// </summary>
public class PythonSettings
{
    public const string SectionName = "Python";

    /// <summary>Absolute path to the Python interpreter (venv preferred).</summary>
    public string PythonPath { get; set; } = string.Empty;

    /// <summary>Absolute path to the signal generation script.</summary>
    public string ScriptPath { get; set; } = string.Empty;

    /// <summary>Absolute path to the daily feature refresh script (D1, step 0).</summary>
    public string RefreshScriptPath { get; set; } = string.Empty;
}
