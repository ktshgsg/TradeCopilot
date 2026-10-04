using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Vision;

/// <summary>
/// Detects chart patterns in captured frames.
/// </summary>
public interface IChartPatternDetector
{
    /// <summary>
    /// True when a model is loaded and detection can run.
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// Detects chart patterns in the frame, sorted by confidence descending.
    /// </summary>
    /// <param name="frame">Frame to analyze.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Detections, or empty when disabled or nothing is found.</returns>
    Task<IReadOnlyList<PatternDetection>> DetectAsync(FrameData frame, CancellationToken ct = default);
}
