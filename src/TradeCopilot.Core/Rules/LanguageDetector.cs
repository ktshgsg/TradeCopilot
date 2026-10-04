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

    /// <summary>
    /// Roman-script Hindi (Hinglish) vocabulary, matched case-insensitively as whole words
    /// and grouped by role for readability. Only whole words count, so "hoga" never matches
    /// inside "hogi" or "hogaya".
    /// </summary>
    private static readonly string[] HinglishMarkerWords =
    [
        // Question words, connectives and discourse markers.
        "kya", "kyunki", "kyun", "kyu", "matlab", "lekin", "magar", "isliye", "toh", "phir",
        "baad", "baat", "aur",

        // Pronouns, possessives, quantifiers and postpositions.
        "mera", "meri", "tera", "teri", "uska", "uski", "unka", "unki", "apna", "apni",
        "apne", "khud", "mujhe", "tujhe", "tumhe", "sab", "sabhi", "sabse", "sabko", "koi",
        "kuch", "wala", "wali", "wale", "walo", "ka", "ki", "ke", "ko", "mein", "pe", "par",

        // Copula and auxiliaries (all person/number/gender forms of "ho" and "hona").
        "hai", "hain", "ho", "hua", "hui", "hue", "hona", "hone", "hota", "hoti", "hote",
        "hoga", "hogi", "hoge", "hongi", "raha", "rahi", "rahe", "rahega", "rahegi",
        "rahunga", "rahungi", "nahi", "haan",

        // Forms of address.
        "bhai", "yaar",

        // Action verbs.
        "kar", "karo", "karna", "karne", "karta", "karti", "karte", "karenge", "karunga",
        "karungi", "dekh", "dekho", "dekha", "dekhna", "dekhta", "dekhti", "soch", "socho",
        "sochna", "bata", "batao", "bataye", "rakh", "rakho", "rakhna", "chalo", "chal",
        "chalta", "chalti", "chalna", "tod", "toda", "todna", "lagana", "lagao", "banao",
        "bana", "banega", "banegi", "banenge", "khareed", "khareedna", "bech", "beko",
        "becho", "bikna", "samajh", "samjho",

        // Participles and predicative adjectives.
        "lag", "laga", "lagta", "lagti", "lagte", "lagne", "milega", "milegi", "mila",
        "mili", "dikh", "dikhata",

        // Evaluation, hedges and intensity.
        "acha", "accha", "achha", "theek", "thik", "sahi", "galat", "badhiya", "mast",
        "shandar", "jugaad", "sach", "sachchi", "mushkil", "aasan", "shayad", "zarur",
        "zaroor", "bilkul", "pakka", "pakki",

        // Degree, time and urgency.
        "jaldi", "abhi", "ab", "thoda", "thodi", "bahut", "bohot", "zyada", "kam", "waqt",

        // Place and direction.
        "upar", "neeche", "andar", "bahar", "aage", "pichle", "peeche", "samne",

        // Attention, lifecycle and finance nouns.
        "dhyan", "dhyaan", "shuru", "khatam", "paisa", "paise", "lakh", "crore", "rupay",
        "rupaye", "sakta", "sakti", "chahiye", "kitna", "kaise", "kab", "kahan",
    ];

    /// <summary>
    /// The Hinglish marker vocabulary, exposed read-only so other components
    /// (for example the speech transliterator) can reuse it instead of duplicating it.
    /// </summary>
    internal static IReadOnlyList<string> HinglishMarkerVocabulary => HinglishMarkerWords;

    private static readonly Regex HinglishMarkers = new(
        @"\b(?:" + string.Join('|', HinglishMarkerWords) + @")\b",
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
