using System.Reflection;
using FluentAssertions;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 2 tests: pattern labels, letterbox preprocessing, and disabled-detector
/// behavior when the YOLO model is absent or unloadable.
/// </summary>
public sealed class VisionTests
{
    /// <summary>The label catalog exposes 11 unique lowercase snake_case names.</summary>
    [Fact]
    public void PatternLabels_Defines11UniqueSnakeCaseLabels()
    {
        var labels = typeof(PatternLabels)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        labels.Should().HaveCount(11);
        labels.Should().OnlyHaveUniqueItems();
        labels.Should().AllSatisfy(v =>
        {
            v.Should().Be(v.ToLowerInvariant());
            v.Should().MatchRegex("^[a-z]+(_[a-z]+)*$");
        });
    }

    /// <summary>A missing model file disables detection and yields empty results.</summary>
    [Fact]
    public async Task YoloDetector_WhenModelMissing_IsDisabled_AndReturnsEmpty()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"tc-missing-{Guid.NewGuid():N}.onnx");
        using var detector = new YoloChartPatternDetector(modelPath: missing);

        detector.Enabled.Should().BeFalse();
        IReadOnlyList<PatternDetection> result = await detector.DetectAsync(SolidFrame(32, 32));
        result.Should().BeEmpty();
    }

    /// <summary>A present-but-unloadable model disables the detector instead of throwing.</summary>
    [Fact]
    public void YoloDetector_WhenModelUnloadable_IsDisabled()
    {
        string path = Path.Combine(Path.GetTempPath(), $"tc-corrupt-{Guid.NewGuid():N}.onnx");
        File.WriteAllText(path, "not an onnx model");
        try
        {
            using var detector = new YoloChartPatternDetector(modelPath: path);
            detector.Enabled.Should().BeFalse();
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>A 1280x640 frame scales by 0.5 and pads 160px top/bottom in CHW planes.</summary>
    [Fact]
    public void Letterbox_ScalesAndPads_IntoChwPlanes()
    {
        var buffer = new float[YoloChartPatternDetector.InputSize * YoloChartPatternDetector.InputSize * 3];
        var frame = new FrameData(1280, 640, 1280 * 4, SolidBgra(1280, 640, 10, 20, 30), DateTime.UtcNow, 1);

        (float scale, int padX, int padY) = YoloChartPatternDetector.Letterbox(frame, buffer);

        scale.Should().Be(0.5f);
        padX.Should().Be(0);
        padY.Should().Be(160);
        int plane = YoloChartPatternDetector.InputSize * YoloChartPatternDetector.InputSize;
        int idx = 160 * YoloChartPatternDetector.InputSize;
        buffer[0].Should().Be(0f);
        buffer[idx].Should().BeApproximately(30f / 255f, 1e-6f);
        buffer[plane + idx].Should().BeApproximately(20f / 255f, 1e-6f);
        buffer[(2 * plane) + idx].Should().BeApproximately(10f / 255f, 1e-6f);
    }

    private static FrameData SolidFrame(int width, int height) =>
        new(width, height, width * 4, SolidBgra(width, height, 10, 20, 30), DateTime.UtcNow, 1);

    private static byte[] SolidBgra(int width, int height, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = 255;
        }

        return pixels;
    }
}
