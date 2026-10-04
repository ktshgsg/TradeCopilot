using Microsoft.Extensions.Logging;

namespace TradeCopilot.Core.Capture;

/// <summary>
/// <see cref="IScreenCapture"/> adapter over <see cref="DxgiScreenCapture"/>'s existing
/// lifecycle members.
/// </summary>
public sealed partial class DxgiScreenCapture
{
    /// <summary>
    /// Starts capturing. The DXGI duplication surface (and therefore the crop rectangle
    /// and frame budget) is fixed when the capturer is constructed, so a differing
    /// <paramref name="region"/> or <paramref name="targetFps"/> is logged and the
    /// configured values continue to be used.
    /// </summary>
    /// <param name="region">Requested region.</param>
    /// <param name="targetFps">Requested frame-rate cap.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once capture has started.</returns>
    Task IScreenCapture.StartAsync(CaptureRegion region, int targetFps, CancellationToken ct)
    {
        if (region != _region || targetFps != _targetFps)
        {
            _log.LogDebug(
                "Capture region/FPS are fixed at construction; ignoring requested {Region}@{Fps} and using {ConfiguredRegion}@{ConfiguredFps}.",
                region,
                targetFps,
                _region,
                _targetFps);
        }

        return StartAsync(ct);
    }

    /// <inheritdoc />
    Task IScreenCapture.StopAsync(CancellationToken ct) => StopAsync(ct);
}
