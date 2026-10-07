namespace TradeCopilot.Core.Privacy;

/// <summary>Privacy-related service options bound from configuration.</summary>
public sealed class PrivacyOptions
{
    /// <summary>Wildcard patterns matched against the foreground window title.</summary>
    public List<string> ExclusionPatterns { get; set; } = [];

    /// <summary>When true, frames with sensitive OCR content are dropped.</summary>
    public bool PauseOnSensitive { get; set; } = true;
}
