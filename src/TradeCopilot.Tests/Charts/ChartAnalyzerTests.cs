using FluentAssertions;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Charts;

namespace TradeCopilot.Tests.Charts;

/// <summary>Tests for <see cref="ChartAnalyzer"/>.</summary>
public sealed class ChartAnalyzerTests
{
    /// <summary>An empty frame produces an empty (IsEmpty) ChartState.</summary>
    [Fact]
    public void EmptyFrame_ReturnsEmptyChartState()
    {
        var analyzer = new ChartAnalyzer();
        var frame = new FrameData(100, 100, 400, new byte[100 * 100 * 4], DateTime.UtcNow, 1);
        var state = analyzer.Analyze(frame);

        state.IsEmpty.Should().BeTrue();
        state.Candles.Should().BeEmpty();
        state.FrameWidth.Should().Be(100);
        state.FrameHeight.Should().Be(100);
    }

    /// <summary>Null frame throws ArgumentNullException.</summary>
    [Fact]
    public void NullFrame_Throws()
    {
        var analyzer = new ChartAnalyzer();
        analyzer.Invoking(a => a.Analyze(null!)).Should().Throw<ArgumentNullException>();
    }

    /// <summary>Enabled is true by default.</summary>
    [Fact]
    public void Enabled_DefaultIsTrue()
    {
        var analyzer = new ChartAnalyzer();
        analyzer.Enabled.Should().BeTrue();
    }
}