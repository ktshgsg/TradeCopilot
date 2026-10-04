using System.Text.RegularExpressions;

namespace TradeCopilot.Core.Rules;

/// <summary>
/// Classifies query text into <see cref="Language.En"/>, <see cref="Language.Hi"/>, or
/// <see cref="Language.Hinglish"/>.
/// </summary>
/// <remarks>
/// Rules: any Devanagari codepoint (U+0900..U+097F) makes the text Hindi; mixed with ASCII
/// letters it is Hinglish. Pure Latin text needs at least two Hinglish marker words
/// (e.g. "kya", "hai", "karo") to be classified Hinglish — a single marker could be
/// coincidence. An empty or blank string is English.
/// </remarks>
public static class LanguageDetector
{
    private const int MinHinglishMarkers = 2;

    private static readonly Regex HinglishMarkers = new(
        @"\b(?:kya|hai|hain|mein|pe|par|ka|ki|ke|ko|karo|kar|batao|bata|nahi|haan|dikh|raha|rahi|rahe|koi|sakta|sakti|hoga|thik|theek|accha|achha|chahiye|kitna|kaise|kab|kahan)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Detects the language of <paramref name="text"/>.
    /// </summary>
    /// <param name="text">Query text; null or blank is treated as English.</param>
    public static Language Detect(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Language.En;
        }

        bool hasDevanagari = false;
        bool hasAsciiLetters = false;
        foreach (char c in text)
        {
            if (c >= '\u0900' && c <= '\u097F')
            {
                hasDevanagari = true;
            }
            else if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
            {
                hasAsciiLetters = true;
            }
        }

        if (hasDevanagari)
        {
            return hasAsciiLetters ? Language.Hinglish : Language.Hi;
        }

        return HinglishMarkers.Matches(text).Count >= MinHinglishMarkers ? Language.Hinglish : Language.En;
    }
}
