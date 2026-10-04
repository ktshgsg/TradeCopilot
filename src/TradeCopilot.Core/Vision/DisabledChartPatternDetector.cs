using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Vision;

/// <summary>
/// No-op detector used when pattern detection is not configured or the model is unavailable.
/// </summary>
public sealed class DisabledChartPatternDetector : IChartPatternDetector
{
    /// <summary>Stateless shared instance.</summary>
    public static DisabledChartPatternDetector Instance { get; } = new();

    private DisabledChartPatternDetector()
    {
    }

    /// <inheritdoc />
    public bool Enabled => false;

    /// <inheritdoc />
    public Task<IReadOnlyList<PatternDetection>> DetectAsync(FrameData frame, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return Task.FromResult<IReadOnlyList<PatternDetection>>(Array.Empty<PatternDetection>());
    }
}
