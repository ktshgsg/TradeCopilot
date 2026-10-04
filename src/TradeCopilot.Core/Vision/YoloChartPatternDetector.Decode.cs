namespace TradeCopilot.Core.Vision;

/// <summary>
/// Raw-output post-processing for <see cref="YoloChartPatternDetector"/>:
/// layout detection, confidence filtering, NMS, and frame-space coordinate mapping.
/// </summary>
public sealed partial class YoloChartPatternDetector
{
    /// <summary>
    /// Converts raw YOLO output into frame-space detections sorted by confidence
    /// descending. Supports [1, 4+classes, N] and [1, N, 4+classes] layouts.
    /// </summary>
    internal static List<PatternDetection> Decode(
        float[] raw, int[] dims, float scale, int padX, int padY, int frameWidth, int frameHeight, int classCount)
    {
        if (dims.Length != 3 || raw.Length == 0)
        {
            return [];
        }

        bool transposed = dims[1] > dims[2];
        int channels = transposed ? dims[2] : dims[1];
        int boxCount = transposed ? dims[1] : dims[2];
        if (channels < 5 || boxCount < 1 || raw.Length < (long)channels * boxCount)
        {
            return [];
        }

        int classes = Math.Min(channels - 4, classCount);
        var candidates = new List<PatternDetection>();
        for (int i = 0; i < boxCount; i++)
        {
            int bestClass = -1;
            float bestScore = ConfidenceThreshold;
            for (int c = 0; c < classes; c++)
            {
                float score = Value(raw, transposed, channels, boxCount, 4 + c, i);
                if (score >= bestScore)
                {
                    bestScore = score;
                    bestClass = c;
                }
            }

            if (bestClass < 0)
            {
                continue;
            }

            float cx = Value(raw, transposed, channels, boxCount, 0, i);
            float cy = Value(raw, transposed, channels, boxCount, 1, i);
            float w = Value(raw, transposed, channels, boxCount, 2, i);
            float h = Value(raw, transposed, channels, boxCount, 3, i);
            candidates.Add(ToFrameCoords(cx, cy, w, h, bestScore, ClassLabels[bestClass], scale, padX, padY, frameWidth, frameHeight));
        }

        candidates.Sort(static (a, b) => b.Confidence.CompareTo(a.Confidence));
        return NonMaxSuppression(candidates);
    }

    private static float Value(float[] raw, bool transposed, int channels, int boxCount, int channel, int index) =>
        transposed ? raw[(index * channels) + channel] : raw[(channel * boxCount) + index];

    private static PatternDetection ToFrameCoords(
        float cx, float cy, float w, float h, float conf, string label,
        float scale, int padX, int padY, int frameWidth, int frameHeight)
    {
        float x = ((cx - (w / 2)) - padX) / scale;
        float y = ((cy - (h / 2)) - padY) / scale;
        float bw = w / scale;
        float bh = h / scale;
        x = Math.Clamp(x, 0, Math.Max(0, frameWidth - 1));
        y = Math.Clamp(y, 0, Math.Max(0, frameHeight - 1));
        bw = Math.Clamp(bw, 1, Math.Max(1, frameWidth - x));
        bh = Math.Clamp(bh, 1, Math.Max(1, frameHeight - y));
        return new PatternDetection(label, conf, new Rect(x, y, bw, bh));
    }

    private static List<PatternDetection> NonMaxSuppression(List<PatternDetection> candidates)
    {
        var kept = new List<PatternDetection>(candidates.Count);
        foreach (PatternDetection candidate in candidates)
        {
            bool suppressed = false;
            foreach (PatternDetection prior in kept)
            {
                if (!string.Equals(prior.Label, candidate.Label, StringComparison.Ordinal))
                {
                    continue;
                }

                if (IoU(prior.BoundingBox, candidate.BoundingBox) > NmsIoU)
                {
                    suppressed = true;
                    break;
                }
            }

            if (!suppressed)
            {
                kept.Add(candidate);
            }
        }

        return kept;
    }

    private static float IoU(Rect a, Rect b)
    {
        float x1 = Math.Max(a.X, b.X);
        float y1 = Math.Max(a.Y, b.Y);
        float x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        float y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
        float inter = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
        float union = (a.Width * a.Height) + (b.Width * b.Height) - inter;
        return union <= 0 ? 0 : inter / union;
    }
}
