using FluentAssertions;
using NSubstitute;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Processing;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 2 tests: FrameProcessor merges OCR and pattern detection through the
/// IOcrEngine and IChartPatternDetector seams (NSubstitute doubles).
/// </summary>
public sealed class ProcessingTests
{
    /// <summary>OCR text and patterns are merged into a single ProcessedFrame.</summary>
    [Fact]
    public async Task FrameProcessor_MergesOcrAndPatterns_IntoProcessedFrame()
    {
        IOcrEngine ocr = Substitute.For<IOcrEngine>();
        ocr.RecognizeAsync(Arg.Any<FrameData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OcrResult("EURUSD 1.0850", [], "en-US")));
        IChartPatternDetector detector = Substitute.For<IChartPatternDetector>();
        detector.Enabled.Returns(true);
        detector.DetectAsync(Arg.Any<FrameData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PatternDetection>>(
                [new PatternDetection(PatternLabels.BullFlag, 0.9f, new Rect(10, 20, 100, 60))]));

        await using var processor = new FrameProcessor(new FrameGate(), ocr, detector);
        var completed = new TaskCompletionSource<ProcessedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.OnProcessed += (_, frame) => completed.TrySetResult(frame);

        var frame = new FrameData(48, 48, 48 * 4, Gradient(48, 48), DateTime.UtcNow, 1);
        processor.Post(frame);

        ProcessedFrame result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        result.Frame.Should().BeSameAs(frame);
        result.Ocr.FullText.Should().Be("EURUSD 1.0850");
        result.Patterns.Should().ContainSingle();
        result.Patterns[0].Label.Should().Be(PatternLabels.BullFlag);
        result.Patterns[0].BoundingBox.Should().Be(new Rect(10, 20, 100, 60));
    }

    /// <summary>A failing detector skips only its frame; later frames still flow through.</summary>
    [Fact]
    public async Task FrameProcessor_WhenDetectorFails_SkipsFrameAndContinues()
    {
        IOcrEngine ocr = Substitute.For<IOcrEngine>();
        ocr.RecognizeAsync(Arg.Any<FrameData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OcrResult("text", [], "en-US")));
        IChartPatternDetector detector = Substitute.For<IChartPatternDetector>();
        detector.Enabled.Returns(true);
        int calls = 0;
        detector.DetectAsync(Arg.Any<FrameData>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    throw new InvalidOperationException("detector failed");
                }

                return Task.FromResult<IReadOnlyList<PatternDetection>>(
                    [new PatternDetection(PatternLabels.DoubleTop, 0.7f, new Rect(0, 0, 10, 10))]);
            });

        await using var processor = new FrameProcessor(new FrameGate(), ocr, detector);
        var completed = new TaskCompletionSource<ProcessedFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        processor.OnProcessed += (_, frame) => completed.TrySetResult(frame);

        processor.Post(new FrameData(48, 48, 48 * 4, Gradient(48, 48), DateTime.UtcNow, 1));
        processor.Post(new FrameData(48, 48, 48 * 4, Checker(48, 48), DateTime.UtcNow, 2));

        ProcessedFrame result = await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        result.Patterns.Should().ContainSingle();
        result.Patterns[0].Label.Should().Be(PatternLabels.DoubleTop);
        calls.Should().Be(2);
    }

    private static byte[] Gradient(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = ((y * width) + x) * 4;
                pixels[o] = (byte)(x * 255 / (width - 1));
                pixels[o + 1] = (byte)(y * 255 / (height - 1));
                pixels[o + 2] = 128;
                pixels[o + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] Checker(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = ((y * width) + x) * 4;
                byte v = (byte)(((x / 8) + (y / 8)) % 2 == 0 ? 255 : 0);
                pixels[o] = v;
                pixels[o + 1] = (byte)(255 - v);
                pixels[o + 2] = 64;
                pixels[o + 3] = 255;
            }
        }

        return pixels;
    }
}
