using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Vision;

/// <summary>
/// Describes the latest captured frame for a user question (visual Q&amp;A) using a
/// local vision model.
/// </summary>
/// <remarks>
/// Implementations are fault-tolerant: <see cref="InitializeAsync"/> never throws, and a
/// model that is missing, unloadable, or incompatible simply leaves <see cref="Enabled"/>
/// false so callers can fall back to the LLM path.
/// </remarks>
public interface IVisionEngine
{
    /// <summary>True when the model loaded and can answer questions.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Loads the model. Never throws; on failure <see cref="Enabled"/> stays false and the
    /// reason is logged.
    /// </summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>
    /// Answers <paramref name="prompt"/> about <paramref name="frame"/>. Returns an empty
    /// string when the engine is disabled, the frame is unusable, or the model fails —
    /// it never throws so the orchestrator can fall through to the LLM.
    /// </summary>
    Task<string> DescribeAsync(FrameData frame, string prompt, CancellationToken ct);
}
