using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Vision;

/// <summary>
/// A permanently disabled <see cref="IVisionEngine"/> used when no vision model is wired
/// up. Mirrors <see cref="DisabledChartPatternDetector"/> so callers and tests can inject a
/// vision stage that never runs and never throws.
/// </summary>
public sealed class DisabledVisionEngine : IVisionEngine
{
    /// <summary>Shared instance; the engine holds no state.</summary>
    public static DisabledVisionEngine Instance { get; } = new();

    private DisabledVisionEngine()
    {
    }

    /// <inheritdoc />
    public bool Enabled => false;

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<string> DescribeAsync(FrameData frame, string prompt, CancellationToken ct) =>
        Task.FromResult(string.Empty);
}
