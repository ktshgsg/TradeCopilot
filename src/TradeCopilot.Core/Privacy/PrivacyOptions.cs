namespace TradeCopilot.Core.Privacy;

/// <summary>Privacy-related service options bound from configuration.</summary>
public sealed class PrivacyOptions
{
    /// <summary>Wildcard patterns matched against the foreground window title.</summary>
    public List<string> ExclusionPatterns { get; set; } = [];

    /// <summary>When true, frames with sensitive OCR content are dropped.</summary>
    public bool PauseOnSensitive { get; set; } = true;

    /// <summary>When true, cached answers are DPAPI-encrypted at rest.</summary>
    public bool EncryptCache { get; set; } = true;

    /// <summary>Cache entries older than this are purged hourly.</summary>
    public int AutoPurgeHours { get; set; } = 24;
}
