using System.Text.Json;

namespace TradeCopilot.UI.Logic;

/// <summary>JSON round-trip persistence for <see cref="UiSettings"/>. Never throws on read.</summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _path;

    /// <summary>Creates a store at the default location: %LOCALAPPDATA%\TradeCopilot\settings.json.</summary>
    public SettingsStore()
        : this(DefaultPath)
    {
    }

    /// <summary>Creates a store at an explicit path (used by tests with a temp directory).</summary>
    /// <param name="path">Full path of the settings.json file.</param>
    public SettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    /// <summary>Default settings path under %LOCALAPPDATA%\TradeCopilot.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TradeCopilot",
        "settings.json");

    /// <summary>Path this store reads and writes.</summary>
    public string FilePath => _path;

    /// <summary>Loads settings, returning defaults when the file is missing or corrupt.</summary>
    /// <returns>The stored settings, normalized.</returns>
    public UiSettings Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return new UiSettings();
            }

            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(_path), JsonOptions)
                ?? new UiSettings();
            settings.Normalize();
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new UiSettings();
        }
    }

    /// <summary>Normalizes and writes the settings to disk, creating the parent directory.</summary>
    /// <param name="settings">Settings to persist.</param>
    public void Save(UiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Normalize();

        string? dir = global::System.IO.Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
