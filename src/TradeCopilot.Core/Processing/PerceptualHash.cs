using System.Numerics;
using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Processing;

/// <summary>
/// 64-bit difference hash (dHash) over frame luminance. Alpha is ignored.
/// </summary>
public static class PerceptualHash
{
    private const int HashWidth = 9;
    private const int HashHeight = 8;

    /// <summary>
    /// Downscales the BGRA frame to 9x8 grayscale and packs 64 adjacent-pixel comparison bits.
    /// </summary>
    /// <param name="frame">Frame to hash.</param>
    /// <returns>64-bit perceptual hash.</returns>
    public static ulong Compute(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frame), "Frame dimensions must be positive.");
        }

        if (frame.Pixels.Length < frame.Stride * frame.Height)
        {
            throw new ArgumentException("Pixel buffer is smaller than Stride * Height.", nameof(frame));
        }

        Span<byte> gray = stackalloc byte[HashWidth * HashHeight];
        for (int y = 0; y < HashHeight; y++)
        {
            int y0 = (y * frame.Height) / HashHeight;
            int y1 = Math.Max(y0 + 1, ((y + 1) * frame.Height) / HashHeight);
            for (int x = 0; x < HashWidth; x++)
            {
                int x0 = (x * frame.Width) / HashWidth;
                int x1 = Math.Max(x0 + 1, ((x + 1) * frame.Width) / HashWidth);
                long b = 0;
                long g = 0;
                long r = 0;
                int n = 0;
                for (int sy = y0; sy < y1; sy++)
                {
                    int row = sy * frame.Stride;
                    for (int sx = x0; sx < x1; sx++)
                    {
                        int o = row + (sx * 4);
                        b += frame.Pixels[o];
                        g += frame.Pixels[o + 1];
                        r += frame.Pixels[o + 2];
                        n++;
                    }
                }

                gray[(y * HashWidth) + x] = (byte)(((r * 77) + (g * 150) + (b * 29)) / (256 * n));
            }
        }

        ulong hash = 0;
        for (int row = 0; row < HashHeight; row++)
        {
            for (int col = 0; col < HashHeight; col++)
            {
                if (gray[(row * HashWidth) + col] > gray[(row * HashWidth) + col + 1])
                {
                    hash |= 1UL << ((row * 8) + col);
                }
            }
        }

        return hash;
    }

    /// <summary>
    /// Counts differing bits between two hashes.
    /// </summary>
    /// <param name="a">First hash.</param>
    /// <param name="b">Second hash.</param>
    /// <returns>Population count of a XOR b.</returns>
    public static int HammingDistance(ulong a, ulong b) => BitOperations.PopCount(a ^ b);
}
