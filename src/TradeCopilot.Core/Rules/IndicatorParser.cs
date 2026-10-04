using System.Globalization;
using System.Text.RegularExpressions;
using TradeCopilot.Core.Ocr;

namespace TradeCopilot.Core.Rules;

/// <summary>
/// Indicator values extracted from a single frame's OCR text.
/// </summary>
/// <param name="Rsi">RSI value, when present.</param>
/// <param name="Macd">MACD line value, when present.</param>
/// <param name="MacdSignal">MACD signal line value, when present.</param>
/// <param name="Price">Last traded price (LTP/Price/CMP/Last), when present.</param>
/// <param name="Volume">Volume in units (K/M/B suffixes expanded), when present.</param>
/// <param name="Extra">
/// Additional uppercase key/value pairs found next to indicator-like labels
/// (e.g. <c>EMA=21</c> → <c>EMA: 21</c>) that are not one of the five named values.
/// </param>
public sealed record ParsedIndicators(
    float? Rsi,
    float? Macd,
    float? MacdSignal,
    float? Price,
    float? Volume,
    IReadOnlyDictionary<string, float> Extra)
{
    /// <summary>No indicators found; used when OCR text is empty or unusable.</summary>
    internal static ParsedIndicators Empty { get; } =
        new(null, null, null, null, null, new Dictionary<string, float>(0, StringComparer.Ordinal));
}

/// <summary>
/// Extracts RSI, MACD, signal, price, and volume values from OCR text using
/// case-insensitive regexes over the full text (never per-word). First match wins.
/// </summary>
public sealed class IndicatorParser
{
    private readonly Regex _rsiRegex = new(
        @"RSI[\s:=\-]+(-?[0-9]+(?:\.[0-9]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Regex _macdRegex = new(
        @"MACD[\s:=\-]+(-?[0-9]+(?:\.[0-9]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Regex _signalRegex = new(
        @"(?:Signal|Sig)[\s:=\-]+(-?[0-9]+(?:\.[0-9]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Regex _priceRegex = new(
        @"(?:LTP|Price|CMP|Last)[\s:=\-]+([0-9]+(?:\.[0-9]+)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Regex _volumeRegex = new(
        @"(?:Vol|Volume)[\s:=\-]+([0-9]+(?:\.[0-9]+)?)\s*([KMB]?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly Regex _extraRegex = new(
        @"(?<key>[A-Za-z]{2,8})[\s:=\-]+(?<value>-?[0-9]+(?:\.[0-9]+)?)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "RSI", "MACD", "SIG", "SIGNAL", "LTP", "PRICE", "CMP", "LAST", "VOL", "VOLUME",
    };

    /// <summary>
    /// Parses indicator values from the full OCR text. Never throws; returns
    /// <see cref="ParsedIndicators.Empty"/> when the OCR result is null or blank.
    /// </summary>
    /// <param name="ocr">Recognized frame text.</param>
    public ParsedIndicators Parse(OcrResult ocr)
    {
        string text = ocr?.FullText ?? string.Empty;
        if (text.Length == 0)
        {
            return ParsedIndicators.Empty;
        }

        return new ParsedIndicators(
            FirstValue(_rsiRegex, text),
            FirstValue(_macdRegex, text),
            FirstValue(_signalRegex, text),
            FirstValue(_priceRegex, text),
            ParseVolume(_volumeRegex.Match(text)),
            ParseExtra(_extraRegex, text));
    }

    private static float? FirstValue(Regex regex, string text)
    {
        Match match = regex.Match(text);
        return match.Success
            && float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : null;
    }

    private static float? ParseVolume(Match match)
    {
        if (!match.Success
            || !float.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
        {
            return null;
        }

        float multiplier = match.Groups[2].Value.ToUpperInvariant() switch
        {
            "K" => 1_000f,
            "M" => 1_000_000f,
            "B" => 1_000_000_000f,
            _ => 1f,
        };
        return value * multiplier;
    }

    private static Dictionary<string, float> ParseExtra(Regex extraRegex, string text)
    {
        Dictionary<string, float>? extra = null;
        foreach (Match match in extraRegex.Matches(text))
        {
            string key = match.Groups["key"].Value.ToUpperInvariant();
            if (KnownKeys.Contains(key)
                || !float.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                continue;
            }

            extra ??= new Dictionary<string, float>(StringComparer.Ordinal);
            extra.TryAdd(key, value);
        }

        return extra ?? new Dictionary<string, float>(0, StringComparer.Ordinal);
    }
}
