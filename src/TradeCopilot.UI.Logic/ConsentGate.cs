namespace TradeCopilot.UI.Logic;

/// <summary>First-run consent gate. Pure file-existence logic so it is unit-testable without WPF.</summary>
public static class ConsentGate
{
    /// <summary>Default flag file: %LOCALAPPDATA%\TradeCopilot\.consent-accepted.</summary>
    public static string DefaultFlagPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TradeCopilot",
        ".consent-accepted");

    /// <summary>Returns true when the consent dialog should be shown (flag file missing).</summary>
    /// <param name="flagPath">Path of the consent flag file.</param>
    /// <returns>True when consent has not yet been accepted.</returns>
    public static bool ShouldShow(string flagPath) => !File.Exists(flagPath);

    /// <summary>Writes the consent flag file, creating the parent directory if needed.</summary>
    /// <param name="flagPath">Path of the consent flag file.</param>
    public static void MarkAccepted(string flagPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(flagPath);
        string? dir = Path.GetDirectoryName(flagPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(flagPath, "accepted");
    }
}
