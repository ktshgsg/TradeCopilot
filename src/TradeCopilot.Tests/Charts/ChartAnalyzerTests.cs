using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Ocr;

namespace TradeCopilot.Tests.Charts;

/// <summary>Tests for <see cref="ChartAnalyzer"/>.</summary>
public sealed class ChartAnalyzerTests
{
    private static readonly float[] ExpectedRawCloses = [80f, 100f, 120f, 140f, 160f];

    private static PriceAxisMapper NewMapper() =>
        new(NullLogger<PriceAxisMapper>.Instance);

    /// <summary>An empty frame produces an empty (IsEmpty) ChartState.</summary>
    [Fact]
    public void EmptyFrame_ReturnsEmptyChartState()
    {
        var analyzer = new ChartAnalyzer(NewMapper());
        var frame = new FrameData(100, 100, 400, new byte[100 * 100 * 4], DateTime.UtcNow, 1);
        var region = new CaptureRegion(0, 0, 100, 100);
        var state = analyzer.Analyze(frame, region, OcrResult.Empty);

        state.IsEmpty.Should().BeTrue();
        state.Candles.Should().BeEmpty();
        state.FrameWidth.Should().Be(100);
        state.FrameHeight.Should().Be(100);
        state.PricesAreMapped.Should().BeFalse();
    }

    /// <summary>Null frame throws ArgumentNullException.</summary>
    [Fact]
    public void NullFrame_Throws()
    {
        var analyzer = new ChartAnalyzer(NewMapper());
        var region = new CaptureRegion(0, 0, 100, 100);
        analyzer.Invoking(a => a.Analyze(null!, region, OcrResult.Empty))
            .Should().Throw<ArgumentNullException>();
    }

    /// <summary>Enabled is true by default.</summary>
    [Fact]
    public void Enabled_DefaultIsTrue()
    {
        var analyzer = new ChartAnalyzer(NewMapper());
        analyzer.Enabled.Should().BeTrue();
    }

    /// <summary>5 candles + OCR axis labels produce mapped (real) prices.</summary>
    [Fact]
    public void FrameWithAxisLabels_PricesAreMapped()
    {
        var analyzer = new ChartAnalyzer(NewMapper());
        var frame = PaintFiveGreenCandles(250, 400);
        var region = new CaptureRegion(0, 0, 250, 400);
        var ocr = new OcrResult(
            "45,100 44,900",
            [
                new OcrWord("45,100", new Windows.Foundation.Rect(5, 100, 60, 12)),
                new OcrWord("44,900", new Windows.Foundation.Rect(5, 300, 60, 12)),
            ],
            "en-US");

        var state = analyzer.Analyze(frame, region, ocr);

        state.PricesAreMapped.Should().BeTrue();
        state.Candles.Should().HaveCount(5);
        state.Candles.Should().OnlyContain(c => c.IsPriceMapped);
        // Axis maps Y=100 -> 45,100 and Y=300 -> 44,900 (slope -1/px), so every
        // candle close (pixel Y in [80, 160]) lands between 45,040 and 45,120.
        state.Candles.Should().OnlyContain(c => c.Close > 40000);
    }

    /// <summary>5 candles + OCR without axis labels keeps raw pixel values.</summary>
    [Fact]
    public void FrameWithoutAxisLabels_KeepsPixelValues()
    {
        var analyzer = new ChartAnalyzer(NewMapper());
        var frame = PaintFiveGreenCandles(250, 400);
        var region = new CaptureRegion(0, 0, 250, 400);
        var ocr = new OcrResult("NIFTY 50 1.25%", [], "en-US");

        var state = analyzer.Analyze(frame, region, ocr);

        state.PricesAreMapped.Should().BeFalse();
        state.Candles.Should().HaveCount(5);
        state.Candles.Should().OnlyContain(c => !c.IsPriceMapped);
        // Raw close equals the candle body top row (pixel Y) of each painted candle.
        state.Candles.Select(c => c.Close)
            .Should().BeEquivalentTo(ExpectedRawCloses);
    }

    /// <summary>
    /// Paints five 8x40 green (bullish) candle bodies on a black BGRA frame.
    /// Body tops sit at Y = 80, 100, 120, 140, 160 (no wicks).
    /// </summary>
    private static FrameData PaintFiveGreenCandles(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        int[] tops = [80, 100, 120, 140, 160];
        for (int c = 0; c < 5; c++)
        {
            int left = 50 + (c * 40);
            int top = tops[c];
            for (int y = top; y < top + 40; y++)
            {
                for (int x = left; x < left + 8; x++)
                {
                    int idx = ((y * width) + x) * 4;
                    pixels[idx + 1] = 255; // G channel (green body)
                    pixels[idx + 3] = 255; // A channel
                }
            }
        }

        return new FrameData(width, height, width * 4, pixels, DateTime.UtcNow, 1);
    }
}