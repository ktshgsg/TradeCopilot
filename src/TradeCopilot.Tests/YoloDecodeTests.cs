using FluentAssertions;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 2 tests: raw YOLO output decoding — layout detection, score filtering,
/// per-class NMS, and frame-space coordinate mapping.
/// </summary>
public sealed class YoloDecodeTests
{
    /// <summary>Classic [1, 4+classes, N] layout: score filter, NMS, coordinate mapping.</summary>
    [Fact]
    public void Decode_ClassicLayout_FiltersScores_SuppressesOverlaps_AndMapsCoordinates()
    {
        const int channels = 15;
        const int boxes = 20;
        var raw = new float[channels * boxes];
        SetBox(raw, channels, boxes, transposed: false, i: 0, cx: 320, cy: 320, w: 200, h: 100, cls: 7, score: 0.90f);
        SetBox(raw, channels, boxes, transposed: false, i: 1, cx: 322, cy: 321, w: 200, h: 100, cls: 7, score: 0.80f);
        SetBox(raw, channels, boxes, transposed: false, i: 2, cx: 100, cy: 100, w: 50, h: 50, cls: 0, score: 0.95f);
        SetBox(raw, channels, boxes, transposed: false, i: 3, cx: 500, cy: 500, w: 80, h: 40, cls: 2, score: 0.20f);

        List<PatternDetection> result = YoloChartPatternDetector.Decode(
            raw, [1, channels, boxes], scale: 1f, padX: 0, padY: 0, frameWidth: 640, frameHeight: 640, classCount: 11);

        result.Should().HaveCount(2);
        result[0].Label.Should().Be(PatternLabels.HeadAndShoulders);
        result[0].Confidence.Should().BeApproximately(0.95f, 1e-6f);
        result[0].BoundingBox.Should().Be(new Rect(75, 75, 50, 50));
        result[1].Label.Should().Be(PatternLabels.BullFlag);
        result[1].Confidence.Should().BeApproximately(0.90f, 1e-6f);
        result[1].BoundingBox.Should().Be(new Rect(220, 270, 200, 100));
    }

    /// <summary>Transposed [1, N, 4+classes] layout decodes with scale and padding applied.</summary>
    [Fact]
    public void Decode_TransposedLayout_DecodesIdenticalBoxes()
    {
        const int channels = 15;
        const int boxes = 20;
        var raw = new float[channels * boxes];
        SetBox(raw, channels, boxes, transposed: true, i: 0, cx: 200, cy: 150, w: 120, h: 60, cls: 8, score: 0.88f);
        SetBox(raw, channels, boxes, transposed: true, i: 1, cx: 400, cy: 400, w: 40, h: 40, cls: 10, score: 0.30f);

        List<PatternDetection> result = YoloChartPatternDetector.Decode(
            raw, [1, boxes, channels], scale: 2f, padX: 50, padY: 30, frameWidth: 320, frameHeight: 320, classCount: 11);

        result.Should().ContainSingle();
        result[0].Label.Should().Be(PatternLabels.BearFlag);
        result[0].BoundingBox.Should().Be(new Rect(45, 45, 60, 30));
    }

    private static void SetBox(
        float[] raw, int channels, int boxes, bool transposed, int i,
        float cx, float cy, float w, float h, int cls, float score)
    {
        Set(raw, channels, boxes, transposed, 0, i, cx);
        Set(raw, channels, boxes, transposed, 1, i, cy);
        Set(raw, channels, boxes, transposed, 2, i, w);
        Set(raw, channels, boxes, transposed, 3, i, h);
        Set(raw, channels, boxes, transposed, 4 + cls, i, score);
    }

    private static void Set(float[] raw, int channels, int boxes, bool transposed, int channel, int index, float value) =>
        raw[transposed ? (index * channels) + channel : (channel * boxes) + index] = value;
}
