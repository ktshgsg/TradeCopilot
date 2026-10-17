using FluentAssertions;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests.Charts;

/// <summary>Tests for <see cref="PatternDetector"/> using synthetic candles.</summary>
public sealed class PatternDetectorTests
{
    private static Candle MakeCandle(int index, float high, float low, float close, bool bullish = true) =>
        new(high, high, low, close, index, bullish, new Rect(index * 10, low, 8, high - low));

    /// <summary>Too few candles returns no patterns.</summary>
    [Fact]
    public void TooFewCandles_ReturnsEmpty()
    {
        var candles = Enumerable.Range(0, 5).Select(i => MakeCandle(i, 100, 90, 95)).ToList();
        PatternDetector.Detect(candles).Should().BeEmpty();
    }

    /// <summary>Null candles returns empty.</summary>
    [Fact]
    public void NullCandles_ReturnsEmpty()
    {
        PatternDetector.Detect(null!).Should().BeEmpty();
    }

    /// <summary>Patterns are sorted by confidence descending.</summary>
    [Fact]
    public void SortedByConfidenceDescending()
    {
        var candles = new List<Candle>();
        for (int i = 0; i < 20; i++)
        {
            candles.Add(MakeCandle(i, 100 - i * 0.5f, 90 + i * 0.2f, 95 + i * 0.1f));
        }
        var patterns = PatternDetector.Detect(candles);
        for (int i = 1; i < patterns.Count; i++)
        {
            patterns[i - 1].Confidence.Should().BeGreaterThanOrEqualTo(patterns[i].Confidence);
        }
    }

    /// <summary>All pattern labels are valid PatternLabels constants.</summary>
    [Fact]
    public void PatternLabels_AreValid()
    {
        var candles = new List<Candle>();
        for (int i = 0; i < 20; i++)
        {
            candles.Add(MakeCandle(i, 100, 90, 95));
        }
        var patterns = PatternDetector.Detect(candles);
        foreach (var p in patterns)
        {
            p.Label.Should().BeOneOf(
                PatternLabels.HeadAndShoulders,
                PatternLabels.InverseHeadAndShoulders,
                PatternLabels.DoubleTop,
                PatternLabels.DoubleBottom,
                PatternLabels.AscendingTriangle,
                PatternLabels.DescendingTriangle,
                PatternLabels.SymmetricalTriangle,
                PatternLabels.BullFlag,
                PatternLabels.BearFlag,
                PatternLabels.SupportBreak,
                PatternLabels.ResistanceBreak);
        }
    }
}