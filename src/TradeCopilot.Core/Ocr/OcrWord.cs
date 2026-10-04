using Windows.Foundation;

namespace TradeCopilot.Core.Ocr;

/// <summary>
/// A single recognized word with its location in frame pixels.
/// </summary>
/// <param name="Text">Recognized word text.</param>
/// <param name="BoundingBox">Word bounds in frame pixels.</param>
public sealed record OcrWord(string Text, Rect BoundingBox);
