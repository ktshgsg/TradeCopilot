using FluentAssertions;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests.Charts;

/// <summary>Tests for <see cref="LevelDetector"/> using synthetic candles.</summary>
public sealed class LevelDetectorTests
{
    private static Candle MakeCandle(int index, float high, float low) =>
        new(high, high, low, 0, index, true, new Rect(index * 10, low, 8, high - low), IsPriceMapped: false);

    /// <summary>Too few candles returns no levels.</summary>
    [Fact]
    public void TooFewCandles_ReturnsEmpty()
    {
        var candles = new[] { MakeCandle(0, 100, 90), MakeCandle(1, 101, 91) };
        LevelDetector.Detect(candles).Should().BeEmpty();
    }

    /// <summary>Repeated highs at the same level form a resistance level.</summary>
    [Fact]
    public void RepeatedHighs_FormResistanceLevel()
    {
        var candles = new List<Candle>();
        for (int i = 0; i < 6; i++)
        {
            candles.Add(MakeCandle(i, 100, 90)); // all touch level at y=100
        }
        var levels = LevelDetector.Detect(candles);
        levels.Should().NotBeEmpty();
        levels.Should().Contain(l => l.Type == LevelType.Resistance && l.TouchCount >= 2);
    }

    /// <summary>Repeated lows at the same level form a support level.</summary>
    [Fact]
    public void RepeatedLows_FormSupportLevel()
    {
        var candles = new List<Candle>();
        for (int i = 0; i < 6; i++)
        {
            candles.Add(MakeCandle(i, 110, 50)); // all touch level at y=50
        }
        var levels = LevelDetector.Detect(candles);
        levels.Should().NotBeEmpty();
        levels.Should().Contain(l => l.Type == LevelType.Support && l.TouchCount >= 2);
    }

    /// <summary>Levels are sorted by touch count descending.</summary>
    [Fact]
    public void SortedByTouchCountDescending()
    {
        var candles = new List<Candle>();
        // Level at y=100: 6 touches
        for (int i = 0; i < 6; i++) candles.Add(MakeCandle(i, 100, 90));
        // Level at y=80: 3 touches
        for (int i = 0; i < 3; i++) candles.Add(MakeCandle(i + 6, 80, 70));
        var levels = LevelDetector.Detect(candles);
        if (levels.Count > 1)
        {
            levels[0].TouchCount.Should().BeGreaterThanOrEqualTo(levels[1].TouchCount);
        }
    }

    /// <summary>Null candles returns empty.</summary>
    [Fact]
    public void NullCandles_ReturnsEmpty()
    {
        LevelDetector.Detect(null!).Should().BeEmpty();
    }
}