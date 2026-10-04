using System.Text;
using TradeCopilot.Core.Rules;

namespace TradeCopilot.Core.Speech;

/// <summary>
/// Converts Roman-script Hinglish to Devanagari so the Piper <c>hi_IN</c> voice reads it
/// with correct phonetics.
/// </summary>
/// <remarks>
/// <para>
/// The dictionary is seeded from <see cref="LanguageDetector"/>'s Hinglish marker
/// vocabulary (the same words used for language detection), so both components agree on
/// what counts as Hinglish. Marker words with no standard Devanagari form (for example
/// <c>mast</c>, <c>jugaad</c>) are deliberately absent and pass through unchanged rather
/// than being mangled.
/// </para>
/// <para>
/// Trading vocabulary in Latin script (RSI, MACD, support, resistance, breakout, ...)
/// is intentionally not transliterated: Piper reads those phonetically either way, and
/// they stay recognisable in the transcript. Input that is already mostly Devanagari is
/// returned untouched.
/// </para>
/// </remarks>
public static class HinglishTransliterator
{
    /// <summary>Number of Roman-to-Devanagari mappings available.</summary>
    public static int DictionaryCount => Dictionary.Count;

    /// <summary>Fraction of Latin letters below which Devanagari input passes through.</summary>
    private const double DevanagariLatinRatio = 0.20;

    /// <summary>Explicit function-word mappings that are not language-detection markers.</summary>
    private static readonly (string Roman, string Devanagari)[] FunctionWords =
    [
        ("aaj", "आज"), ("kal", "कल"), ("yahi", "यही"), ("wahi", "वही"),
        ("iska", "इसका"), ("yahan", "यहाँ"), ("wahan", "वहाँ"), ("niche", "नीचे"),
        ("saath", "साथ"), ("bina", "बिना"), ("saamne", "सामने"), ("peeche", "पीछे"),
        ("ismein", "इसमें"), ("usmein", "उसमें"), ("inmein", "इनमें"), ("unmein", "उनमें"),
        ("humein", "हमें"), ("unhe", "उन्हें"), ("bhai", "भाई"), ("yaar", "यार"),
        ("samajh", "समझ"), ("samjho", "समझो"), ("lag", "लग"), ("laga", "लगा"),
        ("lagta", "लगता"), ("milega", "मिलेगा"), ("milegi", "मिलेगी"),
        ("to", "तो"), ("bhi", "भी"), ("hi", "ही"), ("na", "ना"), ("ya", "या"),
        ("agar", "अगर"), ("jab", "जब"), ("tab", "तब"), ("kyun", "क्यों"),
        ("kyunki", "क्योंकि"), ("kaun", "कौन"), ("kuch", "कुछ"), ("sab", "सब"),
        ("log", "लोग"), ("din", "दिन"), ("baat", "बात"), ("gaya", "गया"),
        ("gayi", "गई"), ("gaye", "गए"), ("do", "दो"), ("dena", "देना"), ("de", "दे"),
        ("lo", "लो"), ("lena", "लेना"), ("le", "ले"), ("ja", "जा"), ("jao", "जाओ"),
        ("aana", "आना"), ("aa", "आ"), ("dekh", "देख"), ("dekho", "देखो"),
        ("dekha", "देखा"), ("sun", "सुन"), ("suno", "सुनो"), ("suna", "सुना"),
        ("bol", "बोल"), ("bolo", "बोलो"), ("bola", "बोला"), ("chala", "चला"),
        ("ruk", "रुक"), ("ruko", "रुको"), ("ruka", "रुका"),
    ];

    /// <summary>Devanagari for the marker words that have a standard written form.</summary>
    private static readonly Dictionary<string, string> MarkerTranslations = new(StringComparer.OrdinalIgnoreCase)
    {
        ["hai"] = "है",
        ["hain"] = "हैं",
        ["ho"] = "हो",
        ["hona"] = "होना",
        ["kya"] = "क्या",
        ["mein"] = "में",
        ["pe"] = "पे",
        ["par"] = "पर",
        ["ka"] = "का",
        ["ki"] = "की",
        ["ke"] = "के",
        ["ko"] = "को",
        ["karo"] = "करो",
        ["kar"] = "कर",
        ["karne"] = "करने",
        ["karna"] = "करना",
        ["karta"] = "करता",
        ["karti"] = "करती",
        ["karte"] = "करते",
        ["batao"] = "बताओ",
        ["bata"] = "बता",
        ["nahi"] = "नहीं",
        ["haan"] = "हाँ",
        ["dikh"] = "दिख",
        ["raha"] = "रहा",
        ["rahi"] = "रही",
        ["rahe"] = "रहे",
        ["koi"] = "कोई",
        ["sakta"] = "सकता",
        ["sakti"] = "सकती",
        ["hoga"] = "होगा",
        ["hogi"] = "होगी",
        ["thik"] = "ठीक",
        ["theek"] = "ठीक",
        ["accha"] = "अच्छा",
        ["achha"] = "अच्छा",
        ["chahiye"] = "चाहिए",
        ["kitna"] = "कितना",
        ["kaise"] = "कैसे",
        ["kab"] = "कब",
        ["kahan"] = "कहाँ",
        ["abhi"] = "अभी",
        ["mera"] = "मेरा",
        ["tera"] = "तेरा",
        ["apna"] = "अपना",
        ["bahut"] = "बहुत",
        ["thoda"] = "थोड़ा",
        ["zyada"] = "ज़्यादा",
        ["kam"] = "कम",
        ["aur"] = "और",
        ["lekin"] = "लेकिन",
        ["magar"] = "मगर",
        ["phir"] = "फिर",
        ["toh"] = "तो",
        ["rakh"] = "रख",
        ["rakho"] = "रखो",
        ["chal"] = "चल",
        ["chalo"] = "चलो",
        ["upar"] = "ऊपर",
        ["neeche"] = "नीचे",
        ["andar"] = "अंदर",
        ["bahar"] = "बाहर",
        ["mujhe"] = "मुझे",
        ["tujhe"] = "तुझे",
        ["ab"] = "अब",
        ["jaldi"] = "जल्दी",
        ["dhyan"] = "ध्यान",
        ["waqt"] = "वक्त",
        ["dhyaan"] = "ध्यान",
        ["shuru"] = "शुरू",
        ["khatam"] = "खत्म",
        ["paisa"] = "पैसा",
        ["paise"] = "पैसे",
        ["lakh"] = "लाख",
        ["crore"] = "करोड़",
        ["hota"] = "होता",
        ["hoti"] = "होती",
        ["hote"] = "होते",
        ["hoge"] = "होगे",
        ["hongi"] = "होंगी",
        ["hua"] = "हुआ",
        ["hui"] = "हुई",
        ["hue"] = "हुए",
        ["rahega"] = "रहेगा",
        ["rahegi"] = "रहेगी",
        ["rahunga"] = "रहूंगा",
        ["rahungi"] = "रहूंगी",
        ["thodi"] = "थोड़ी",
        ["mila"] = "मिला",
        ["mili"] = "मिली",
        ["badhiya"] = "बढ़िया",
        ["shayad"] = "शायद",
        ["pakka"] = "पक्का",
        ["pakki"] = "पक्की",
        ["bilkul"] = "बिल्कुल",
        ["zaroor"] = "ज़रूर",
        ["mushkil"] = "मुश्किल",
        ["aasan"] = "आसान",
        ["sahi"] = "सही",
        ["galat"] = "गलत",
        ["milta"] = "मिलता",
        ["rupay"] = "रुपया",
        ["rupaye"] = "रुपये",
    };

    /// <summary>
    /// The seeded map. Declared after <see cref="FunctionWords"/> and
    /// <see cref="MarkerTranslations"/> because C# runs static field initializers in
    /// declaration order.
    /// </summary>
    private static readonly Dictionary<string, string> Dictionary = BuildDictionary();

    private static Dictionary<string, string> BuildDictionary()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Seed from the shared language-detection vocabulary so both components agree on
        // what "Hinglish" means; markers without a standard form are simply absent and
        // pass through unchanged.
        foreach (string marker in LanguageDetector.HinglishMarkerVocabulary)
        {
            if (MarkerTranslations.TryGetValue(marker, out string? devanagari))
            {
                map[marker] = devanagari;
            }
        }

        foreach ((string roman, string devanagari) in FunctionWords)
        {
            map[roman] = devanagari;
        }

        return map;
    }

    /// <summary>
    /// Converts Roman-script Hinglish to Devanagari, preserving whitespace, punctuation,
    /// numbers, and any word missing from the dictionary. Devanagari-dominant input is
    /// returned unchanged.
    /// </summary>
    /// <param name="text">Text to transliterate; null or empty returns empty.</param>
    /// <returns>The transliterated text.</returns>
    public static string ToDevanagari(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (IsDevanagariDominant(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length + 16);
        int index = 0;
        while (index < text.Length)
        {
            if (char.IsLetter(text[index]) && text[index] < 0x0080)
            {
                int start = index;
                while (index < text.Length && char.IsLetter(text[index]) && text[index] < 0x0080)
                {
                    index++;
                }

                string word = text[start..index];
                builder.Append(Dictionary.TryGetValue(word, out string? devanagari) ? devanagari : word);
            }
            else
            {
                builder.Append(text[index]);
                index++;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// True when the text already contains Devanagari and fewer than
    /// <see cref="DevanagariLatinRatio"/> of its letters are Latin, so it should pass
    /// through untouched.
    /// </summary>
    private static bool IsDevanagariDominant(string text)
    {
        int devanagari = 0;
        int latin = 0;
        foreach (char c in text)
        {
            if (c is >= 'ऀ' and <= 'ॿ')
            {
                devanagari++;
            }
            else if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                latin++;
            }
        }

        if (devanagari == 0)
        {
            return false;
        }

        int total = devanagari + latin;
        return total == 0 || latin / (double)total < DevanagariLatinRatio;
    }
}
