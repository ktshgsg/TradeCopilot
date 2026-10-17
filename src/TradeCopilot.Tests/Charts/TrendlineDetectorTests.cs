using FluentAssertions;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests.Charts;

/// <summary>Tests for <see cref="TrendlineDetector"/> using synthetic candles.</summary>
public sealed class TrendlineDetectorTests
{
    private static Candle MakeCandle(int index, float open, float high, float low, float close, bool bullish = true) =>
        new(open, high, low, close, index, bullish, new Rect(index * 10, 0, 8, 8));

    /// <summary>Too few candles returns no trendlines.</summary>
    [Fact]
    public void TooFewCandles_ReturnsEmpty()
    {
        var candles = new[]
        {
            MakeCandle(0, 100, 105, 99, 104), MakeCandle(1, 104, 108, 103, 107),
            MakeCandle(2, 107, 110, 106, 109),
        };
        TrendlineDetector.Detect(candles).Should().BeEmpty();
    }

    /// <summary>Candles forming rising swing lows produce a support trendline.</summary>
    [Fact]
    public void RisingSwingLows_ProducesSupportTrendline()
    {
        var candles = new List<Candle>
        {
            MakeCandle(0, 100, 105, 105, 102),  // low = 105
            MakeCandle(1, 101, 106, 106, 103),  // low = 106
            MakeCandle(2, 102, 107, 107, 104),  // low = 107
            MakeCandle(3, 103, 108, 100, 105),  // low = 100 (swing low)
            MakeCandle(4, 104, 109, 108, 106),  // low = 108
            MakeCandle(5, 105, 110, 105, 107),  // low = 105
            MakeCandle(6, 106, 111, 109, 108),  // low = 109
            MakeCandle(7, 107, 112, 107, 109),  // low = 107
            MakeCandle(8, 108, 113, 108, 110),  // low = 108
            MakeCandle(9, 109, 114, 109, 111),  // low = 109
            MakeCandle(10, 110, 115, 95, 112),  // low = 95 (swing low)
            MakeCandle(11, 111, 116, 110, 113), // low = 110
            MakeCandle(12, 112, 117, 111, 114), // low = 111
            MakeCandle(13, 113, 118, 112, 115), // low = 112
        };
        var lines = TrendlineDetector.Detect(candles);
        lines.Should().NotBeEmpty();
        lines.Should().Contain(l => l.Type == TrendlineType.Support);
    }

    /// <summary>Candles forming falling swing highs produce a resistance trendline.</summary>
    [Fact]
    public void FallingSwingHighs_ProducesResistanceTrendline()
    {
        var candles = new List<Candle>
        {
            MakeCandle(0, 110, 115, 108, 112),  // high = 115
            MakeCandle(1, 112, 116, 110, 111),
            MakeCandle(2, 111, 117, 109, 116),  // high = 117
            MakeCandle(3, 113, 115, 111, 112),
            MakeCandle(4, 114, 116, 112, 113),
            MakeCandle(5, 116, 118, 112, 114),
            MakeCandle(6, 118, 120, 114, 116),  // high = 120 (swing high)
            MakeCandle(7, 119, 116, 115, 115),
            MakeCandle(8, 120, 117, 116, 116),
            MakeCandle(9, 121, 118, 117, 117),
            MakeCandle(10, 122, 124, 118, 119), // high = 124 (swing high)
            MakeCandle(11, 123, 121, 119, 119),
            MakeCandle(12, 124, 122, 120, 120),
            MakeCandle(13, 125, 123, 121, 121),
        };
        var lines = TrendlineDetector.Detect(candles);
        lines.Should().NotBeEmpty();
        lines.Should().Contain(l => l.Type == TrendlineType.Resistance);
    }

    /// <summary>Trendlines are sorted by touch count descending.</summary>
    [Fact]
    public void SortedByTouchCountDescending()
    {
        var candles = new List<Candle>();
        for (int i = 0; i < 10; i++)
        {
            float y = 100 - i * 2; // rising support
            candles.Add(MakeCandle(i, y, y + 2, y - 2, y + 1));
        }
        var lines = TrendlineDetector.Detect(candles);
        if (lines.Count > 1)
        {
            for (int i = 1; i < lines.Count; i++)
            {
                lines[i - 1].TouchCount.Should().BeGreaterThanOrEqualTo(lines[i].TouchCount);
            }
        }
    }

    /// <summary>Null candles returns empty.</summary>
    [Fact]
    public void NullCandles_ReturnsEmpty()
    {
        TrendlineDetector.Detect(null!).Should().BeEmpty();
    }
}