#pragma warning disable IDE0005 // FrameData is referenced but analyzer doesn't detect it
using System.Runtime.InteropServices;
using TradeCopilot.Core.Capture;
#pragma warning restore IDE0005

using OpenCvSharp;
using Rect = TradeCopilot.Core.Vision.Rect;

namespace TradeCopilot.Core.Charts;

/// <summary>
/// Extracts candlesticks from a captured chart frame using classical computer vision:
/// BGRA frame → OpenCV Mat → HSV colour masks for red/green bodies → connected
/// components → filtering → sorting by x-position.
/// </summary>
/// <remarks>
/// All OHLC values are in <b>pixel space</b> (y-coordinates, smaller y = higher price).
/// Callers such as <see cref="ChartAnalyzer"/> convert pixel-space OHLC to actual
/// prices using the chart's price axis (from OCR). <see cref="Candle.IsBullish"/> is true
/// when the close pixel is above the open pixel (higher price).
/// </remarks>
public static class CandleExtractor
{
    /// <summary>Minimum candle body area in pixels; smaller blobs are noise.</summary>
    private const int MinBodyArea = 20;

    /// <summary>Maximum candle body area in pixels; larger blobs are not candle bodies.</summary>
    private const int MaxBodyArea = 5000;

    /// <summary>Minimum body width in pixels.</summary>
    private const int MinBodyWidth = 3;

    /// <summary>Minimum body height in pixels.</summary>
    private const int MinBodyHeight = 3;

    /// <summary>Maximum aspect ratio (width/height) for a candle body.</summary>
    private const float MaxBodyAspectRatio = 5f;

    /// <summary>
    /// Extracts candles from a frame. Returns candles sorted by bar index ascending.
    /// Returns an empty list when no candles are found.
    /// </summary>
    /// <param name="frame">Captured BGRA frame.</param>
    /// <returns>Detected candles in pixel-space OHLC, sorted by index.</returns>
    public static IReadOnlyList<Candle> Extract(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.Pixels is null or { Length: 0 })
        {
            return [];
        }

        using var bgra = new Mat(frame.Height, frame.Width, MatType.CV_8UC4);
        unsafe
        {
            Marshal.Copy(frame.Pixels, 0, (IntPtr)bgra.DataPointer, frame.Pixels.Length);
        }

        // BGRA → BGR → HSV for colour-based candle body segmentation.
        using var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

        using var hsv = new Mat();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        // Red = bearish bodies, green = bullish bodies. Red wraps at hue=0/180 so
        // we build two red masks and OR them together.
        using var redMask1 = new Mat();
        using var redMask2 = new Mat();
        using var redMask = new Mat();
        using var greenMask = new Mat();

        Cv2.InRange(hsv, new Scalar(0, 120, 70), new Scalar(10, 255, 255), redMask1);
        Cv2.InRange(hsv, new Scalar(160, 120, 70), new Scalar(180, 255, 255), redMask2);
        Cv2.BitwiseOr(redMask1, redMask2, redMask);

        Cv2.InRange(hsv, new Scalar(35, 100, 100), new Scalar(85, 255, 255), greenMask);

        // Connected components on each colour separately so we know bullish vs bearish.
        var candles = new List<Candle>();
        int index = 0;

        ExtractFromMask(frame, greenMask, true, candles, ref index);
        ExtractFromMask(frame, redMask, false, candles, ref index);

        // Sort by x-position (bar index) ascending.
        candles.Sort((a, b) => a.BoundingBox.X.CompareTo(b.BoundingBox.X));
        for (int i = 0; i < candles.Count; i++)
        {
            var c = candles[i];
            candles[i] = c with { Index = i };
        }

        return candles;
    }

    /// <summary>Finds candle bodies in one colour mask and converts them to candles.</summary>
    private static void ExtractFromMask(
        FrameData frame,
        Mat mask,
        bool isBullish,
        List<Candle> candles,
        ref int index)
    {
        using var labels = new Mat(mask.Rows, mask.Cols, MatType.CV_32S);
        using var stats = new Mat(mask.Rows, mask.Cols, MatType.CV_32S);
        using var centroids = new Mat(mask.Rows, mask.Cols, MatType.CV_64F);

        int n = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids, (PixelConnectivity)8, MatType.CV_32S);

        for (int i = 1; i < n; i++) // skip background label 0
        {
            int left = stats.Get<int>(i, 0);
            int top = stats.Get<int>(i, 1);
            int width = stats.Get<int>(i, 2);
            int height = stats.Get<int>(i, 3);
            int area = stats.Get<int>(i, 4);

            if (!IsValidBody(left, top, width, height, area))
            {
                continue;
            }

            // Candle body: top = open (bullish) / close (bearish), bottom = close (bullish) / open (bearish).
            float bodyTop = top;
            float bodyBottom = top + height;

            // Scan up/down from the body to find wick extremes.
            float high = FindWickExtreme(frame, mask, left, width, (int)bodyTop, searchUp: true);
            float low = FindWickExtreme(frame, mask, left, width, (int)bodyBottom, searchUp: false);

            float openPx, closePx;
            if (isBullish)
            {
                openPx = bodyBottom; // open at bottom of body (lower price)
                closePx = bodyTop;   // close at top of body (higher price)
            }
            else
            {
                openPx = bodyTop;    // open at top of body (higher price)
                closePx = bodyBottom; // close at bottom of body (lower price)
            }

            var bbox = new Rect(left, top, width, height);
            candles.Add(new Candle(
                Open: openPx,
                High: high,
                Low: low,
                Close: closePx,
                Index: index++,
                IsBullish: isBullish,
                BoundingBox: bbox,
                IsPriceMapped: false));
        }
    }

    private static bool IsValidBody(int left, int top, int width, int height, int area)
    {
        if (area < MinBodyArea || area > MaxBodyArea)
        {
            return false;
        }
        if (width < MinBodyWidth || height < MinBodyHeight)
        {
            return false;
        }
        if (height > 0 && (float)width / height > MaxBodyAspectRatio)
        {
            return false;
        }
        return true;
    }

    /// <summary>
    /// Scans vertically from a starting row within the body's column range to find
    /// the wick extreme (highest or lowest pixel belonging to the same candle).
    /// </summary>
    private static float FindWickExtreme(
        FrameData frame,
        Mat mask,
        int left,
        int width,
        int startRow,
        bool searchUp)
    {
        int limit = searchUp ? 0 : frame.Height;
        int step = searchUp ? -1 : 1;

        for (int row = startRow + step; row != limit; row += step)
        {
            bool anyPixel = false;
            for (int col = left; col < left + width; col++)
            {
                if (mask.Get<byte>(row, col) > 0)
                {
                    anyPixel = true;
                    break;
                }
            }
            if (!anyPixel)
            {
                // Wick ended one pixel before this row.
                return searchUp ? row + 1 : row - 1;
            }
        }
        return searchUp ? 0 : frame.Height - 1;
    }
}