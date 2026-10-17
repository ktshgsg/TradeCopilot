using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Charts;

/// <summary>
/// Detects horizontal support and resistance levels by clustering candle highs and lows
/// in pixel space, then counting how many candles touch each level.
/// </summary>
public static class LevelDetector
{
    /// <summary>Maximum pixel distance between two price levels to merge into one cluster.</summary>
    private const float LevelMergeThreshold = 6f;

    /// <summary>Maximum pixel distance from a candle's high/low to a level to count as a touch.</summary>
    private const float TouchThreshold = 4f;

    /// <summary>Minimum touch count for a level to be reported.</summary>
    private const int MinTouches = 2;

    /// <summary>Maximum number of levels to return (top by touch count).</summary>
    private const int MaxLevels = 5;

    /// <summary>
    /// Detects support/resistance levels from candles. Resistance clusters form near
    /// repeated swing highs; support clusters form near repeated swing lows.
    /// </summary>
    /// <param name="candles">Candles in pixel space.</param>
    /// <returns>Up to <see cref="MaxLevels"/> levels, sorted by touch count descending.</returns>
    public static IReadOnlyList<SupportResistance> Detect(IReadOnlyList<Candle> candles)
    {
        if (candles is null || candles.Count < 4)
        {
            return [];
        }

        // Collect all high and low pixel positions as candidate level points.
        var levelPoints = new List<float>();
        foreach (var c in candles)
        {
            levelPoints.Add(c.High);
            levelPoints.Add(c.Low);
        }

        levelPoints.Sort();

        // Cluster nearby points into levels.
        var clusters = ClusterPoints(levelPoints);

        var levels = new List<SupportResistance>();
        foreach (var cluster in clusters)
        {
            float levelY = cluster.Average();
            int touches = 0;
            foreach (var c in candles)
            {
                if (Math.Abs(c.High - levelY) <= TouchThreshold ||
                    Math.Abs(c.Low - levelY) <= TouchThreshold)
                {
                    touches++;
                }
            }

            if (touches >= MinTouches)
            {
                // A level near the top of the chart (small y) is resistance; near the bottom is support.
                LevelType type = levelY < candles[0].BoundingBox.Y + candles[0].BoundingBox.Height / 2f
                    ? LevelType.Resistance
                    : LevelType.Support;

                levels.Add(new SupportResistance(
                    Price: levelY,
                    Type: type,
                    TouchCount: touches,
                    BoundingBox: new Rect(0, levelY - 1, candles[0].BoundingBox.Width, 2),
                    IsPriceMapped: false));
            }
        }

        levels.Sort((a, b) => b.TouchCount.CompareTo(a.TouchCount));
        return levels.Take(MaxLevels).ToList();
    }

    /// <summary>Clusters sorted points where adjacent points are within the merge threshold.</summary>
    private static List<List<float>> ClusterPoints(List<float> points)
    {
        var clusters = new List<List<float>>();
        if (points.Count == 0)
        {
            return clusters;
        }

        var current = new List<float> { points[0] };
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i] - current[^1] <= LevelMergeThreshold)
            {
                current.Add(points[i]);
            }
            else
            {
                clusters.Add(current);
                current = new List<float> { points[i] };
            }
        }
        clusters.Add(current);
        return clusters;
    }
}