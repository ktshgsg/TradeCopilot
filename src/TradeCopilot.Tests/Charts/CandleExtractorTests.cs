using FluentAssertions;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Charts;

namespace TradeCopilot.Tests.Charts;

/// <summary>Tests for <see cref="CandleExtractor"/> using synthetic chart frames.</summary>
public sealed class CandleExtractorTests
{
    /// <summary>Builds a BGRA frame with a single vertical colored bar.</summary>
    private static FrameData FrameWithBar(int width, int height, int barX, int barWidth, int barTop, int barBottom, byte b, byte g, byte r)
    {
        var pixels = new byte[width * height * 4];
        for (int y = barTop; y < barBottom; y++)
        {
            for (int x = barX; x < barX + barWidth; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
        }
        return new FrameData(width, height, width * 4, pixels, DateTime.UtcNow, 1);
    }

    /// <summary>A frame with no candles returns an empty list.</summary>
    [Fact]
    public void EmptyFrame_ReturnsEmpty()
    {
        var frame = new FrameData(100, 100, 400, new byte[100 * 100 * 4], DateTime.UtcNow, 1);
        var candles = CandleExtractor.Extract(frame);
        candles.Should().BeEmpty();
    }

    /// <summary>A green vertical bar is detected as one bullish candle.</summary>
    [Fact]
    public void GreenBar_DetectedAsBullishCandle()
    {
        // Pure green: B=0, G=255, R=0 → HSV hue ≈ 60 (green range)
        var frame = FrameWithBar(100, 100, 40, 6, 30, 70, 0, 255, 0);
        var candles = CandleExtractor.Extract(frame);

        candles.Should().HaveCount(1);
        candles[0].IsBullish.Should().BeTrue();
        candles[0].BoundingBox.X.Should().Be(40);
        candles[0].BoundingBox.Width.Should().Be(6);
        candles[0].Index.Should().Be(0);
    }

    /// <summary>A red vertical bar is detected as one bearish candle.</summary>
    [Fact]
    public void RedBar_DetectedAsBearishCandle()
    {
        // Pure red: B=0, G=0, R=255 → HSV hue ≈ 0 (red range)
        var frame = FrameWithBar(100, 100, 40, 6, 30, 70, 0, 0, 255);
        var candles = CandleExtractor.Extract(frame);

        candles.Should().HaveCount(1);
        candles[0].IsBullish.Should().BeFalse();
        candles[0].BoundingBox.X.Should().Be(40);
        candles[0].Index.Should().Be(0);
    }

    /// <summary>Multiple bars are sorted by x-position.</summary>
    [Fact]
    public void MultipleBars_SortedByX()
    {
        var pixels = new byte[100 * 100 * 4];
        // Bar at x=60 (should be index 1 after sorting)
        FillBar(pixels, 100, 60, 5, 20, 80, 0, 255, 0);
        // Bar at x=20 (should be index 0 after sorting)
        FillBar(pixels, 100, 20, 5, 30, 70, 0, 0, 255);

        var frame = new FrameData(100, 100, 400, pixels, DateTime.UtcNow, 1);
        var candles = CandleExtractor.Extract(frame);

        candles.Should().HaveCount(2);
        candles[0].BoundingBox.X.Should().Be(20);
        candles[0].Index.Should().Be(0);
        candles[1].BoundingBox.X.Should().Be(60);
        candles[1].Index.Should().Be(1);
    }

    /// <summary>Tiny bars below MinBodyArea are filtered out.</summary>
    [Fact]
    public void TinyBar_FilteredOut()
    {
        var frame = FrameWithBar(100, 100, 40, 2, 30, 35, 0, 255, 0); // 2*5=10 < MinBodyArea 20
        var candles = CandleExtractor.Extract(frame);
        candles.Should().BeEmpty();
    }

    /// <summary>An undersized pixel buffer throws instead of corrupting native memory.</summary>
    [Fact]
    public void UndersizedBuffer_ThrowsArgumentException()
    {
        var frame = new FrameData(100, 100, 400, new byte[100], DateTime.UtcNow, 1);
        Action act = () => CandleExtractor.Extract(frame);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*too small for declared dimensions*");
    }

    /// <summary>Invalid dimensions return empty instead of crashing the process.</summary>
    [Fact]
    public void InvalidDimensions_ReturnEmpty()
    {
        var frame = new FrameData(0, 100, 400, new byte[100], DateTime.UtcNow, 1);
        CandleExtractor.Extract(frame).Should().BeEmpty();
    }

    private static void FillBar(byte[] pixels, int width, int barX, int barWidth, int barTop, int barBottom, byte b, byte g, byte r)
    {
        for (int y = barTop; y < barBottom; y++)
        {
            for (int x = barX; x < barX + barWidth; x++)
            {
                int i = (y * width + x) * 4;
                pixels[i] = b;
                pixels[i + 1] = g;
                pixels[i + 2] = r;
                pixels[i + 3] = 255;
            }
        }
    }
}