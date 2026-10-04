namespace TradeCopilot.Core.Rules;

/// <summary>Language of a user query as classified by <see cref="LanguageDetector"/>.</summary>
public enum Language
{
    /// <summary>English (Latin script).</summary>
    En,

    /// <summary>Hindi (Devanagari script, U+0900..U+097F).</summary>
    Hi,

    /// <summary>Hinglish: Hindi words in Latin script, or mixed Devanagari + Latin script.</summary>
    Hinglish,
}
