namespace TradeCopilot.UI.Logic;

/// <summary>UI-side settings, bound from <c>appsettings.json</c>.</summary>
public sealed class UiOptions
{
    /// <summary>Named-pipe endpoint the service hosts.</summary>
    public string PipeName { get; set; } = "TradeCopilot.Pipe";

    /// <summary>Whether assistant answers are also spoken aloud.</summary>
    public bool VoiceOutputEnabled { get; set; }

    /// <summary>Preferred answer language: "auto", "en", or "hi".</summary>
    public string LanguagePreference { get; set; } = "auto";
}
