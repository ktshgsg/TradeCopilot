using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Ocr;

public sealed partial class WindowsOcrEngine
{
    /// <summary>
    /// Box-downsamples BGRA to at most <paramref name="maxWidth"/> wide. Returns the original
    /// buffer (no copy) when no downscale is needed.
    /// </summary>
    internal static (int Width, int Height, byte[] Pixels) Downscale(FrameData frame, int maxWidth)
    {
        if (frame.Width <= maxWidth)
        {
            return (frame.Width, frame.Height, frame.Pixels);
        }

        double scale = (double)maxWidth / frame.Width;
        int width = maxWidth;
        int height = Math.Max(1, (int)(frame.Height * scale));
        var dst = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int y0 = (y * frame.Height) / height;
            int y1 = Math.Max(y0 + 1, ((y + 1) * frame.Height) / height);
            for (int x = 0; x < width; x++)
            {
                int x0 = (x * frame.Width) / width;
                int x1 = Math.Max(x0 + 1, ((x + 1) * frame.Width) / width);
                long b = 0;
                long g = 0;
                long r = 0;
                long a = 0;
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
                        a += frame.Pixels[o + 3];
                        n++;
                    }
                }

                int o2 = ((y * width) + x) * 4;
                dst[o2] = (byte)(b / n);
                dst[o2 + 1] = (byte)(g / n);
                dst[o2 + 2] = (byte)(r / n);
                dst[o2 + 3] = (byte)(a / n);
            }
        }

        return (width, height, dst);
    }
}
