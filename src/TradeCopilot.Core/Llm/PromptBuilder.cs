using System.Globalization;
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

    private const string SystemPrompt =
        "You are TradeCopilot, an expert trading assistant for Indian retail traders.\n\n"
        + "RULES:\n"
        + "1. Respond in the SAME language the user asked in. If the user writes in Hindi or Hinglish, respond in Hinglish (Roman script preferred, Devanagari acceptable). If English, respond in English.\n"
        + "2. Keep answers under 3 sentences unless asked for detail.\n"
        + "3. Cite exact numbers from the chart context.\n"
        + "4. Never invent indicator values. If not provided, say you can't see it.\n"
        + "5. Use 'may', 'suggests', 'likely'. Never give certainty.\n"
        + "6. Never give financial advice as a promise of returns.";

    /// <summary>Returns the fixed system prompt.</summary>
    public static string BuildSystemPrompt() => SystemPrompt;

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
    /// <param name="cachedContext">Previously cached answer for similar context, when available.</param>
    public static string BuildUserPrompt(
        string query,
        OcrResult? ocr,
        IReadOnlyList<PatternDetection> patterns,
        string? cachedContext)
    {
        string ocrText = TruncateCodepoints(ocr?.FullText ?? string.Empty, MaxOcrCodepoints);
        string ocrLine = ocrText.Length == 0 ? "OCR:" : "OCR: " + ocrText;
        string patternText = patterns is null || patterns.Count == 0
            ? "none"
            : string.Join(
                ", ",
                patterns.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Label} ({p.Confidence:F2})")));
        string cached = string.IsNullOrWhiteSpace(cachedContext) ? "none" : cachedContext;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[CHART CONTEXT]\n{ocrLine}\nPatterns: {patternText}\nCached: {cached}\n[USER QUERY]\n{query}");
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
