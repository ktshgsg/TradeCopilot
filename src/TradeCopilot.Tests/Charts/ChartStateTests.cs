using FluentAssertions;
using System.Drawing;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests.Charts;

/// <summary>Tests for <see cref="ChartState"/> and related records.</summary>
public sealed class ChartStateTests
{
    private static Candle MakeCandle(int index, float open, float high, float low, float close, bool isBullish = true) =>
        new(open, high, low, close, index, isBullish, new Rect(index * 10, 0, 8, 8), IsPriceMapped: false);

    /// <summary>An empty ChartState reports IsEmpty = true.</summary>
    [Fact]
    public void EmptyState_IsEmpty()
    {
        var state = new ChartState([], [], [], [], 100, 100);
        state.IsEmpty.Should().BeTrue();
    }

    /// <summary>A state with at least 5 candles is not empty.</summary>
    [Fact]
    public void WithCandles_IsNotEmpty()
    {
        var state = new ChartState(
            [MakeCandle(0, 100, 105, 99, 104), MakeCandle(1, 104, 108, 103, 107),
             MakeCandle(2, 107, 110, 106, 109), MakeCandle(3, 109, 112, 108, 111),
             MakeCandle(4, 111, 115, 110, 114)],
            [], [], [], 100, 100);
        state.IsEmpty.Should().BeFalse();
    }

    /// <summary>Candles are immutable; constructing with different values produces a new instance.</summary>
    [Fact]
    public void Candle_DifferentValues_CreatesNewInstance()
    {
        var c = MakeCandle(0, 100, 105, 99, 104);
        var modified = new Candle(90f, c.High, c.Low, c.Close, c.Index, c.IsBullish, c.BoundingBox, c.IsPriceMapped);
        c.Open.Should().Be(100f);
        modified.Open.Should().Be(90f);
        modified.IsBullish.Should().BeTrue();
    }

    /// <summary>Trendline records are value-equatable.</summary>
    [Fact]
    public void Trendline_Equality()
    {
        var points = new List<PointF> { new(10, 20), new(30, 40) };
        var a = new Trendline(TrendlineType.Resistance, 0.5f, 10f, 3, points);
        var b = new Trendline(TrendlineType.Resistance, 0.5f, 10f, 3, points);
        a.Should().Be(b);
    }

    /// <summary>SupportResistance records are value-equatable.</summary>
    [Fact]
    public void SupportResistance_Equality()
    {
        var a = new SupportResistance(100f, LevelType.Support, 3, new Rect(0, 90, 100, 5), IsPriceMapped: false);
        var b = new SupportResistance(100f, LevelType.Support, 3, new Rect(0, 90, 100, 5), IsPriceMapped: false);
        a.Should().Be(b);
        a.Type.Should().Be(LevelType.Support);
    }

    /// <summary>DetectedPattern records carry label, confidence, and bounding box.</summary>
    [Fact]
    public void DetectedPattern_HasLabelConfidenceAndBox()
    {
        var p = new DetectedPattern(PatternLabels.DoubleTop, 0.85f, new Rect(10, 20, 30, 40));
        p.Label.Should().Be(PatternLabels.DoubleTop);
        p.Confidence.Should().Be(0.85f);
        p.BoundingBox.Should().Be(new Rect(10, 20, 30, 40));
    }
}