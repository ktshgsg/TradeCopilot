using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Ocr;

/// <summary>
/// Recognizes text from captured frames.
/// </summary>
public interface IOcrEngine
{
    /// <summary>BCP-47 tags of installed recognizer languages.</summary>
    IReadOnlyList<string> SupportedLanguages { get; }

    /// <summary>True when a recognizer backend is available.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Recognizes text in the frame, downscaling to 1920 wide first for speed.
    /// </summary>
    /// <param name="frame">Frame to recognize.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Recognition result, or empty when unavailable.</returns>
    Task<OcrResult> RecognizeAsync(FrameData frame, CancellationToken ct = default);
}
