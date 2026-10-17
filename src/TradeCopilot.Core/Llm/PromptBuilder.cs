using System.Globalization;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Llm;

/// <summary>
/// Builds the fixed system prompt, the chart-context user prompt, and the Qwen2.5
/// chat prompt used by <see cref="LlamaSharpEngine"/>.
/// </summary>
public static class PromptBuilder
{
    /// <summary>Maximum OCR characters (Unicode codepoints) included in the user prompt.</summary>
    public const int MaxOcrCodepoints = 1500;

    /// <summary>Returns the fixed system prompt.</summary>
    public static string BuildSystemPrompt() =>
        "You are TradeCopilot, an AI trading assistant.\n"
        + "Reply in the user's language (English, Hindi, or Hinglish).\n"
        + "Keep answers under 2 sentences and only cite numbers you see.";

    /// <summary>
    /// Builds the Qwen2.5 chat prompt; the model generates the reply from the
    /// trailing <c>&lt;|im_start|&gt;assistant</c> marker.
    /// </summary>
    /// <param name="system">System prompt text.</param>
    /// <param name="user">User prompt text.</param>
    public static string BuildChatPrompt(string system, string user) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{user}<|im_end|>\n<|im_start|>assistant\n");

    /// <summary>
    /// Builds the user prompt: chart context (OCR, patterns, cached answer) followed by
    /// the user query. The OCR text is truncated at <see cref="MaxOcrCodepoints"/>
    /// codepoints; missing context is rendered as "none".
    /// </summary>
    /// <param name="query">User query.</param>
    /// <param name="ocr">Latest OCR result, when available.</param>
    /// <param name="patterns">Detected patterns, when available.</param>
    /// <param name="chartState">Classical CV chart analysis result, when available.</param>
    /// <param name="cachedContext">Previously cached answer for similar context, when available.</param>
    public static string BuildUserPrompt(
        string query,
        OcrResult? ocr,
        IReadOnlyList<PatternDetection> patterns,
        ChartState? chartState = null,
        string? cachedContext = null)
    {
        string ocrText = TruncateCodepoints(ocr?.FullText ?? string.Empty, MaxOcrCodepoints);
        string ocrLine = ocrText.Length == 0 ? "OCR:" : "OCR: " + ocrText;
        string patternText = patterns is null || patterns.Count == 0
            ? "none"
            : string.Join(
                ", ",
                patterns.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Label} ({p.Confidence:F2})")));

        string chartLine;
        if (chartState is null || chartState.IsEmpty)
        {
            chartLine = "Chart: none";
        }
        else
        {
            var candleInfo = chartState.Candles.Count > 0
                ? $"{chartState.Candles.Count} candles"
                : "no candles";
            var trendlineInfo = chartState.Trendlines.Count > 0
                ? string.Join(", ", chartState.Trendlines.Select(t => $"{t.Type.ToString().ToLowerInvariant()} trendline ({t.TouchCount} touches)"))
                : "no trendlines";
            var levelInfo = chartState.Levels.Count > 0
                ? string.Join(", ", chartState.Levels.Select(l => $"{l.Type.ToString().ToLowerInvariant()} at px={l.Price:F0} ({l.TouchCount} touches)"))
                : "no levels";
            var patternInfo = chartState.Patterns.Count > 0
                ? string.Join(", ", chartState.Patterns.Select(p => $"{p.Label} ({p.Confidence:F2})"))
                : "no patterns";
            chartLine = $"Chart: {candleInfo}; {trendlineInfo}; {levelInfo}; {patternInfo}";
        }

        string cached = string.IsNullOrWhiteSpace(cachedContext) ? "none" : cachedContext;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[CHART CONTEXT]\n{ocrLine}\nPatterns: {patternText}\n{chartLine}\nCached: {cached}\n[USER QUERY]\n{query}");
    }

    /// <summary>Truncates at a codepoint boundary without splitting surrogate pairs.</summary>
    private static string TruncateCodepoints(string text, int maxCodepoints)
    {
        int codepoints = 0;
        int index = 0;
        while (index < text.Length)
        {
            bool isPair = char.IsHighSurrogate(text[index])
                && index + 1 < text.Length
                && char.IsLowSurrogate(text[index + 1]);
            codepoints++;
            index += isPair ? 2 : 1;
            if (codepoints == maxCodepoints)
            {
                return text[..index];
            }
        }

        return text;
    }
}
