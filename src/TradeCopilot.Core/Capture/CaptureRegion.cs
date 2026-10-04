namespace TradeCopilot.Core.Capture;

/// <summary>
/// Pixel region of the desktop to capture. Coordinates are in desktop pixels.
/// </summary>
/// <param name="X">Left edge of the region.</param>
/// <param name="Y">Top edge of the region.</param>
/// <param name="Width">Region width in pixels.</param>
/// <param name="Height">Region height in pixels.</param>
public readonly record struct CaptureRegion(int X, int Y, int Width, int Height);
