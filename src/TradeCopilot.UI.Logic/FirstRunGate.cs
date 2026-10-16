namespace TradeCopilot.UI.Logic;

/// <summary>
/// First-run model-download gate. Pure file-existence logic (mirrors
/// <see cref="ConsentGate"/>): the wizard is shown only while the flag file is missing
/// AND required models are absent, so "Skip for now" suppresses it permanently and a
/// completed download suppresses it naturally.
/// </summary>
public static class FirstRunGate
{
    /// <summary>Default flag file: %LOCALAPPDATA%\TradeCopilot\.firstrun-complete.</summary>
    public static string DefaultFlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TradeCopilot",
        ".firstrun-complete");

    /// <summary>Returns true when the wizard should be shown.</summary>
    /// <param name="flagPath">Path of the first-run flag file.</param>
    /// <param name="modelsPresent">Whether every required model file is already on disk.</param>
    /// <returns>True when the flag file is missing and models are not all present.</returns>
    public static bool ShouldShow(string flagPath, bool modelsPresent) =>
        !File.Exists(flagPath) && !modelsPresent;

    /// <summary>Writes the first-run flag file, creating the parent directory if needed.</summary>
    /// <param name="flagPath">Path of the first-run flag file.</param>
    public static void MarkComplete(string flagPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagPath);
        string? dir = Path.GetDirectoryName(flagPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(flagPath, "complete");
    }
}