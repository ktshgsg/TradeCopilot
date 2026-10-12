namespace TradeCopilot.UI.Logic;

/// <summary>
/// User-editable UI preferences persisted to %LOCALAPPDATA%\TradeCopilot\settings.json.
/// Region and FPS are stored for v1.1 service sync; the service reads its own config in v1.
/// </summary>
public sealed class UiSettings
{
    /// <summary>Capture region X (0 = unset; 0,0,0,0 means full screen on the service side).</summary>
    public int RegionX { get; set; }

    /// <summary>Capture region Y.</summary>
    public int RegionY { get; set; }

    /// <summary>Capture region width in pixels (0 = unset).</summary>
    public int RegionWidth { get; set; }

    /// <summary>Capture region height in pixels (0 = unset).</summary>
    public int RegionHeight { get; set; }

    /// <summary>Target capture frame rate, 1–10 (default 2).</summary>
    public int Fps { get; set; } = 2;

    /// <summary>UI language preference: "auto", "en", or "hi". Reserved in v1 (service detects language).</summary>
    public string Language { get; set; } = "auto";

    /// <summary>Whether assistant answers should be spoken aloud (reserved in v1).</summary>
    public bool VoiceOutputEnabled { get; set; }

    /// <summary>Whether push-to-talk voice input is enabled (gates the overlay mic button).</summary>
    public bool PushToTalkEnabled { get; set; } = true;

    /// <summary>ASR voice model name. v1 ships only hi_IN-rohan-medium.</summary>
    public string VoiceModel { get; set; } = "hi_IN-rohan-medium";

    /// <summary>Service-side cache encryption flag, shown read-only for transparency.</summary>
    public bool EncryptCache { get; set; } = true;

    /// <summary>Window-title exclusion patterns shown in the Privacy tab.</summary>
    public List<string> ExclusionPatterns { get; set; } = new();

    /// <summary>Clamps values into valid ranges so a hand-edited file cannot poison the UI.</summary>
    public void Normalize()
    {
        Fps = Math.Clamp(Fps, 1, 10);
        Language = Language switch
        {
            "en" or "hi" => Language,
            _ => "auto",
        };

        VoiceModel = string.IsNullOrWhiteSpace(VoiceModel) ? "hi_IN-rohan-medium" : VoiceModel.Trim();
        ExclusionPatterns ??= new List<string>();
    }
}
