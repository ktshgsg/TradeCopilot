namespace TradeCopilot.Core.Capture;

/// <summary>
/// Captures desktop frames for the processing pipeline.
/// </summary>
/// <remarks>
/// Introduced with the orchestrator so the service can register a concrete capturer and
/// the named-pipe commands (pause/resume) can be served against a substitutable
/// abstraction. <see cref="DxgiScreenCapture"/> is the production implementation.
/// </remarks>
public interface IScreenCapture : IDisposable
{
    /// <summary>True while frames are being captured.</summary>
    bool IsCapturing { get; }

    /// <summary>Raised on the capture thread for each acquired frame.</summary>
    event EventHandler<FrameData>? OnFrameCaptured;

    /// <summary>
    /// Starts capturing <paramref name="region"/> at up to <paramref name="targetFps"/>.
    /// Idempotent: calling it while already capturing has no effect.
    /// </summary>
    /// <param name="region">Region to crop; width/height of 0 mean the full output.</param>
    /// <param name="targetFps">Frame-rate cap.</param>
    /// <param name="ct">Cancellation token tied to the capture loop.</param>
    /// <returns>A task that completes once capture has started.</returns>
    Task StartAsync(CaptureRegion region, int targetFps, CancellationToken ct = default);

    /// <summary>Stops capturing and joins the capture thread.</summary>
    /// <param name="ct">Cancellation token (reserved).</param>
    /// <returns>A task that completes once capture has stopped.</returns>
    Task StopAsync(CancellationToken ct = default);
}
