using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Processing;

/// <summary>
/// A frame enriched with OCR text and pattern detections.
/// </summary>
/// <param name="Frame">Source frame.</param>
/// <param name="Ocr">Recognized text.</param>
/// <param name="Patterns">Detected chart patterns (empty when detection is disabled).</param>
/// <param name="ProcessedAt">UTC time processing finished.</param>
public sealed record ProcessedFrame(
    FrameData Frame,
    OcrResult Ocr,
    IReadOnlyList<PatternDetection> Patterns,
    DateTime ProcessedAt);
