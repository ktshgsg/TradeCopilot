namespace TradeCopilot.Core.Ocr;

/// <summary>
/// Text recognized from a single frame.
/// </summary>
/// <param name="FullText">All recognized text.</param>
/// <param name="Words">Recognized words with bounding boxes.</param>
/// <param name="LanguageTag">BCP-47 tag of the recognizer language, e.g. "en-US".</param>
public sealed record OcrResult(string FullText, IReadOnlyList<OcrWord> Words, string LanguageTag)
{
    /// <summary>Empty result used when the OCR engine is unavailable or recognition fails.</summary>
    public static OcrResult Empty { get; } = new(string.Empty, Array.Empty<OcrWord>(), string.Empty);
}
