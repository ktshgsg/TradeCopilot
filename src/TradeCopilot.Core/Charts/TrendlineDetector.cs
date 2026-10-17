using System.Drawing;

namespace TradeCopilot.Core.Charts;

/// <summary>
/// Detects trendlines by fitting lines to swing highs (resistance) and swing lows
/// (support) from detected candles, then counting how many candle wicks touch each line.
/// </summary>
public static class TrendlineDetector
{
    /// <summary>Number of neighbouring candles on each side required for a swing point.</summary>
    private const int SwingWindow = 3;

    /// <summary>Maximum pixel distance from a candle's wick to a line to count as a touch.</summary>
    private const float TouchThreshold = 4f;

    /// <summary>Minimum touch count for a trendline to be reported.</summary>
    private const int MinTouches = 2;

    /// <summary>Maximum number of trendlines to return (top by touch count).</summary>
    private const int MaxTrendlines = 3;

    /// <summary>
    /// Detects trendlines from candles. Resistance lines connect swing highs; support
    /// lines connect swing lows. Results are sorted by touch count descending.
    /// </summary>
    /// <param name="candles">Candles in pixel space (y down, smaller y = higher price).</param>
    /// <returns>Up to <see cref="MaxTrendlines"/> trendlines, sorted by touch count descending.</returns>
    public static IReadOnlyList<Trendline> Detect(IReadOnlyList<Candle> candles)
    {
        if (candles is null || candles.Count < SwingWindow * 2 + 2)
        {
            return [];
        }

        var swingHighs = FindSwingPoints(candles, findHighs: true);
        var swingLows = FindSwingPoints(candles, findHighs: false);

        var lines = new List<Trendline>();

        if (swingHighs.Count >= 2)
        {
            var line = FitLine(swingHighs, TrendlineType.Resistance, candles);
            if (line.TouchCount >= MinTouches)
            {
                lines.Add(line);
            }
        }

        if (swingLows.Count >= 2)
        {
            var line = FitLine(swingLows, TrendlineType.Support, candles);
            if (line.TouchCount >= MinTouches)
            {
                lines.Add(line);
            }
        }

        lines.Sort((a, b) => b.TouchCount.CompareTo(a.TouchCount));
        return lines.Take(MaxTrendlines).ToList();
    }

    /// <summary>Finds swing highs or lows: a candle whose high/low exceeds all neighbours within the window.</summary>
    private static List<PointF> FindSwingPoints(IReadOnlyList<Candle> candles, bool findHighs)
    {
        var points = new List<PointF>();
        int n = candles.Count;

        for (int i = SwingWindow; i < n - SwingWindow; i++)
        {
            bool isSwing = true;
            float current = findHighs ? candles[i].High : candles[i].Low;

            for (int j = i - SwingWindow; j <= i + SwingWindow; j++)
            {
                if (j == i) continue;
                float other = findHighs ? candles[j].High : candles[j].Low;
                if (findHighs ? current <= other : current >= other)
                {
                    isSwing = false;
                    break;
                }
            }

            if (isSwing)
            {
                // Use the candle body centre x as the point x, and the swing price y.
                float x = candles[i].BoundingBox.X + candles[i].BoundingBox.Width / 2f;
                float y = findHighs ? candles[i].High : candles[i].Low;
                points.Add(new PointF(x, y));
            }
        }

        return points;
    }

    /// <summary>Fits a least-squares line through points and counts candle wick touches.</summary>
    private static Trendline FitLine(
        List<PointF> points,
        TrendlineType type,
        IReadOnlyList<Candle> candles)
    {
        // Least-squares: y = slope * x + intercept
        int n = points.Count;
        float sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        foreach (var p in points)
        {
            sumX += p.X;
            sumY += p.Y;
            sumXY += p.X * p.Y;
            sumXX += p.X * p.X;
        }

        float denom = n * sumXX - sumX * sumX;
        if (Math.Abs(denom) < 1e-6f)
        {
            // Vertical line: cannot fit y = mx + b; return a degenerate line.
            return new Trendline(type, 0f, 0f, 0, points);
        }

        float slope = (n * sumXY - sumX * sumY) / denom;
        float intercept = (sumY - slope * sumX) / n;

        // Count touches: candle high/low within TouchThreshold of the line at the candle's x.
        int touches = 0;
        foreach (var c in candles)
        {
            float cx = c.BoundingBox.X + c.BoundingBox.Width / 2f;
            float lineY = slope * cx + intercept;
            float highDiff = Math.Abs(c.High - lineY);
            float lowDiff = Math.Abs(c.Low - lineY);

            if (highDiff <= TouchThreshold || lowDiff <= TouchThreshold)
            {
                touches++;
            }
        }

        return new Trendline(type, slope, intercept, touches, points);
    }
}