using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Charts;

/// <summary>
/// Detects classical chart patterns from a sequence of candles using swing-point
/// analysis. Patterns are reported with confidence in [0, 1] and bounding boxes
/// in frame pixels.
/// </summary>
public static class PatternDetector
{
    /// <summary>Minimum number of candles required for pattern detection.</summary>
    private const int MinCandles = 10;

    /// <summary>Maximum y-difference (pixels) between two swing points to consider them at the same level.</summary>
    private const float SameLevelThreshold = 12f;

    /// <summary>Minimum confidence for a pattern to be reported.</summary>
    private const float MinConfidence = 0.5f;

    /// <summary>
    /// Detects chart patterns from candles.
    /// </summary>
    /// <param name="candles">Candles sorted by index ascending, in pixel space.</param>
    /// <returns>Detected patterns, sorted by confidence descending.</returns>
    public static IReadOnlyList<DetectedPattern> Detect(IReadOnlyList<Candle> candles)
    {
        if (candles is null || candles.Count < MinCandles)
        {
            return [];
        }

        var patterns = new List<DetectedPattern>();

        patterns.AddRange(DetectDoubleTop(candles));
        patterns.AddRange(DetectDoubleBottom(candles));
        patterns.AddRange(DetectHeadAndShoulders(candles));
        patterns.AddRange(DetectBullFlag(candles));
        patterns.AddRange(DetectBearFlag(candles));

        patterns.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));
        return patterns;
    }

    /// <summary>Double top: two swing highs at approximately the same level with a swing low between.</summary>
    private static IEnumerable<DetectedPattern> DetectDoubleTop(IReadOnlyList<Candle> candles)
    {
        var swingHighs = FindSwingHighs(candles);
        if (swingHighs.Count < 2)
        {
            yield break;
        }

        for (int i = 0; i < swingHighs.Count - 1; i++)
        {
            for (int j = i + 1; j < swingHighs.Count; j++)
            {
                var first = swingHighs[i];
                var second = swingHighs[j];

                if (Math.Abs(first.Y - second.Y) > SameLevelThreshold)
                {
                    continue;
                }

                bool hasDip = false;
                for (int k = first.Index + 1; k < second.Index; k++)
                {
                    if (candles[k].Low > first.Y + 8f)
                    {
                        hasDip = true;
                        break;
                    }
                }

                if (!hasDip)
                {
                    continue;
                }

                float confidence = 0.75f - Math.Abs(first.Y - second.Y) * 0.01f;
                confidence = Math.Clamp(confidence, 0f, 1f);

                if (confidence < MinConfidence)
                {
                    continue;
                }

                int left = (int)Math.Min(first.X, second.X) - 10;
                int right = (int)Math.Max(first.X, second.X) + 10;
                int top = (int)Math.Min(first.Y, second.Y) - 10;
                int bottom = (int)(Math.Max(candles[first.Index].Low, candles[second.Index].Low) + 10);

                yield return new DetectedPattern(
                    Label: PatternLabels.DoubleTop,
                    Confidence: confidence,
                    BoundingBox: new Rect(left, top, right - left, bottom - top));
            }
        }
    }

    /// <summary>Double bottom: mirror of double top.</summary>
    private static IEnumerable<DetectedPattern> DetectDoubleBottom(IReadOnlyList<Candle> candles)
    {
        var swingLows = FindSwingLows(candles);
        if (swingLows.Count < 2)
        {
            yield break;
        }

        for (int i = 0; i < swingLows.Count - 1; i++)
        {
            for (int j = i + 1; j < swingLows.Count; j++)
            {
                var first = swingLows[i];
                var second = swingLows[j];

                if (Math.Abs(first.Y - second.Y) > SameLevelThreshold)
                {
                    continue;
                }

                bool hasRally = false;
                for (int k = first.Index + 1; k < second.Index; k++)
                {
                    if (candles[k].High < first.Y - 8f)
                    {
                        hasRally = true;
                        break;
                    }
                }

                if (!hasRally)
                {
                    continue;
                }

                float confidence = 0.75f - Math.Abs(first.Y - second.Y) * 0.01f;
                confidence = Math.Clamp(confidence, 0f, 1f);

                if (confidence < MinConfidence)
                {
                    continue;
                }

                int left = (int)Math.Min(first.X, second.X) - 10;
                int right = (int)Math.Max(first.X, second.X) + 10;
                int bottom = (int)Math.Max(first.Y, second.Y) + 10;
                int top = (int)(Math.Min(candles[first.Index].High, candles[second.Index].High) - 10);

                yield return new DetectedPattern(
                    Label: PatternLabels.DoubleBottom,
                    Confidence: confidence,
                    BoundingBox: new Rect(left, top, right - left, bottom - top));
            }
        }
    }

    /// <summary>Head and shoulders: three swing highs, middle highest, shoulders roughly equal.</summary>
    private static IEnumerable<DetectedPattern> DetectHeadAndShoulders(IReadOnlyList<Candle> candles)
    {
        var swingHighs = FindSwingHighs(candles);
        if (swingHighs.Count < 3)
        {
            yield break;
        }

        for (int i = 0; i < swingHighs.Count - 2; i++)
        {
            var leftShoulder = swingHighs[i];
            var head = swingHighs[i + 1];
            var rightShoulder = swingHighs[i + 2];

            if (!(head.Y < leftShoulder.Y - 4f && head.Y < rightShoulder.Y - 4f))
            {
                continue;
            }

            if (Math.Abs(leftShoulder.Y - rightShoulder.Y) > SameLevelThreshold)
            {
                continue;
            }

            float confidence = 0.7f;
            confidence = Math.Clamp(confidence, 0f, 1f);

            if (confidence < MinConfidence)
            {
                continue;
            }

            int left = (int)Math.Min(leftShoulder.X, rightShoulder.X) - 10;
            int right = (int)Math.Max(leftShoulder.X, rightShoulder.X) + 10;
            int top = (int)(head.Y - 10);
            int bottom = (int)(Math.Max(leftShoulder.Y, rightShoulder.Y) + 10);

            yield return new DetectedPattern(
                Label: PatternLabels.HeadAndShoulders,
                Confidence: confidence,
                BoundingBox: new Rect(left, top, right - left, bottom - top));
        }
    }

    /// <summary>Bull flag: strong upward move followed by a downward-sloping consolidation.</summary>
    private static IEnumerable<DetectedPattern> DetectBullFlag(IReadOnlyList<Candle> candles)
    {
        if (candles.Count < 12)
        {
            yield break;
        }

        int mid = candles.Count / 2;

        float firstHalfMove = candles[mid - 1].Close - candles[0].Close;
        if (firstHalfMove <= 0)
        {
            yield break;
        }

        float secondHalfMove = candles[candles.Count - 1].Close - candles[mid].Close;
        if (secondHalfMove >= -2f)
        {
            yield break;
        }

        int bullishCount = 0;
        for (int i = 0; i < mid; i++)
        {
            if (candles[i].IsBullish) bullishCount++;
        }
        if ((float)bullishCount / mid < 0.6f)
        {
            yield break;
        }

        float confidence = 0.65f;
        if (confidence < MinConfidence)
        {
            yield break;
        }

        int left = (int)candles[0].BoundingBox.X - 5;
        int right = (int)(candles[candles.Count - 1].BoundingBox.X + candles[candles.Count - 1].BoundingBox.Width + 5);
        int top = (int)candles.Select(c => c.High).Min() - 10;
        int bottom = (int)candles.Select(c => c.Low).Max() + 10;

        yield return new DetectedPattern(
            Label: PatternLabels.BullFlag,
            Confidence: confidence,
            BoundingBox: new Rect(left, top, right - left, bottom - top));
    }

    /// <summary>Bear flag: mirror of bull flag.</summary>
    private static IEnumerable<DetectedPattern> DetectBearFlag(IReadOnlyList<Candle> candles)
    {
        if (candles.Count < 12)
        {
            yield break;
        }

        int mid = candles.Count / 2;

        float firstHalfMove = candles[0].Close - candles[mid - 1].Close;
        if (firstHalfMove <= 0)
        {
            yield break;
        }

        float secondHalfMove = candles[candles.Count - 1].Close - candles[mid].Close;
        if (secondHalfMove <= 2f)
        {
            yield break;
        }

        int bearishCount = 0;
        for (int i = 0; i < mid; i++)
        {
            if (!candles[i].IsBullish) bearishCount++;
        }
        if ((float)bearishCount / mid < 0.6f)
        {
            yield break;
        }

        float confidence = 0.65f;
        if (confidence < MinConfidence)
        {
            yield break;
        }

        int left = (int)candles[0].BoundingBox.X - 5;
        int right = (int)(candles[candles.Count - 1].BoundingBox.X + candles[candles.Count - 1].BoundingBox.Width + 5);
        int top = (int)candles.Select(c => c.High).Min() - 10;
        int bottom = (int)candles.Select(c => c.Low).Max() + 10;

        yield return new DetectedPattern(
            Label: PatternLabels.BearFlag,
            Confidence: confidence,
            BoundingBox: new Rect(left, top, right - left, bottom - top));
    }

    /// <summary>Finds swing highs: candles whose high exceeds all neighbours within a 3-candle window.</summary>
    private static List<(float X, float Y, int Index)> FindSwingHighs(IReadOnlyList<Candle> candles)
    {
        var result = new List<(float X, float Y, int Index)>();
        int window = 3;
        int n = candles.Count;

        for (int i = window; i < n - window; i++)
        {
            bool isSwing = true;
            float current = candles[i].High;

            for (int j = i - window; j <= i + window; j++)
            {
                if (j == i) continue;
                if (current <= candles[j].High)
                {
                    isSwing = false;
                    break;
                }
            }

            if (isSwing)
            {
                float x = candles[i].BoundingBox.X + candles[i].BoundingBox.Width / 2f;
                result.Add((x, current, i));
            }
        }

        return result;
    }

    /// <summary>Finds swing lows: mirror of swing highs.</summary>
    private static List<(float X, float Y, int Index)> FindSwingLows(IReadOnlyList<Candle> candles)
    {
        var result = new List<(float X, float Y, int Index)>();
        int window = 3;
        int n = candles.Count;

        for (int i = window; i < n - window; i++)
        {
            bool isSwing = true;
            float current = candles[i].Low;

            for (int j = i - window; j <= i + window; j++)
            {
                if (j == i) continue;
                if (current >= candles[j].Low)
                {
                    isSwing = false;
                    break;
                }
            }

            if (isSwing)
            {
                float x = candles[i].BoundingBox.X + candles[i].BoundingBox.Width / 2f;
                result.Add((x, current, i));
            }
        }

        return result;
    }
}