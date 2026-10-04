namespace TradeCopilot.Core.Capture;

/// <summary>
/// A single captured frame in 32-bit BGRA pixel format.
/// </summary>
/// <remarks>
/// The <see cref="Pixels"/> buffer is rented from the shared array pool and ownership
/// transfers to the <c>OnFrameCaptured</c> subscriber when the event fires. Only the first
/// <c>Stride * Height</c> bytes are valid; the array itself may be longer. Subscribers must
/// copy the data if they retain it past the event handler, and must not return it to the pool.
/// </remarks>
/// <param name="Width">Frame width in pixels.</param>
/// <param name="Height">Frame height in pixels.</param>
/// <param name="Stride">Bytes per row (width * 4).</param>
/// <param name="Pixels">BGRA pixel data (first Stride * Height bytes valid).</param>
/// <param name="TimestampUtc">UTC time the frame was captured.</param>
/// <param name="SequenceNumber">Monotonic frame sequence number.</param>
public sealed record FrameData(
    int Width,
    int Height,
    int Stride,
    byte[] Pixels,
    DateTime TimestampUtc,
    long SequenceNumber);
