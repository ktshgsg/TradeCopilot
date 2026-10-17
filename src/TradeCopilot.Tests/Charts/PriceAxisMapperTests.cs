using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Ocr;

namespace TradeCopilot.Tests.Charts;

/// <summary>Tests for <see cref="PriceAxisMapper"/>.</summary>
public sealed class PriceAxisMapperTests
{
    private const int FrameWidth = 1000;

    private static PriceAxisMapper NewMapper() =>
        new(NullLogger<PriceAxisMapper>.Instance);

    private static OcrResult OcrWith(params OcrWord[] words) =>
        new(string.Join(' ', words.Select(w => w.Text)), words, "en-US");

    private static OcrWord LeftLabel(string text, double y) =>
        new(text, new Windows.Foundation.Rect(10, y, 60, 12));

    private static OcrWord RightLabel(string text, double y) =>
        new(text, new Windows.Foundation.Rect(950, y, 60, 12));

    /// <summary>Two labels at distinct Y positions produce a correct linear map.</summary>
    [Fact]
    public void TwoLabels_BuildCorrectMapper()
    {
        var ocr = OcrWith(LeftLabel("45,100", 100), LeftLabel("44,900", 300));
        var mapper = NewMapper().BuildMapper(ocr, FrameWidth);

        mapper.Should().NotBeNull();
        mapper!(100).Should().BeApproximately(45100f, 0.01f);
        mapper(300).Should().BeApproximately(44900f, 0.01f);
        mapper(200).Should().BeApproximately(45000f, 0.01f);
    }

    /// <summary>Two labels at the same Y position (invalid axis) return null.</summary>
    [Fact]
    public void SameYPosition_ReturnsNull()
    {
        var ocr = OcrWith(LeftLabel("45,100", 100), LeftLabel("44,900", 100));
        NewMapper().BuildMapper(ocr, FrameWidth).Should().BeNull();
    }

    /// <summary>A single label returns null.</summary>
    [Fact]
    public void SingleLabel_ReturnsNull()
    {
        var ocr = OcrWith(LeftLabel("45,100", 100));
        NewMapper().BuildMapper(ocr, FrameWidth).Should().BeNull();
    }

    /// <summary>No labels returns null.</summary>
    [Fact]
    public void NoLabels_ReturnsNull()
    {
        NewMapper().BuildMapper(OcrResult.Empty, FrameWidth).Should().BeNull();
    }

    /// <summary>Thousands separators are stripped when parsing.</summary>
    [Fact]
    public void CommaSeparatedLabels_AreParsed()
    {
        var ocr = OcrWith(LeftLabel("45,100", 50), LeftLabel("46,200", 250));
        var mapper = NewMapper().BuildMapper(ocr, FrameWidth);

        mapper.Should().NotBeNull();
        mapper!(50).Should().BeApproximately(45100f, 0.01f);
        mapper(250).Should().BeApproximately(46200f, 0.01f);
    }

    /// <summary>Forex-style decimal labels are parsed correctly.</summary>
    [Fact]
    public void DecimalLabels_AreParsed()
    {
        var ocr = OcrWith(LeftLabel("1.0523", 100), LeftLabel("1.0567", 300));
        var mapper = NewMapper().BuildMapper(ocr, FrameWidth);

        mapper.Should().NotBeNull();
        mapper!(100).Should().BeApproximately(1.0523f, 0.0001f);
        mapper(300).Should().BeApproximately(1.0567f, 0.0001f);
    }

    /// <summary>Labels on the right edge of the frame are accepted.</summary>
    [Fact]
    public void RightEdgeLabels_AreAccepted()
    {
        var ocr = OcrWith(RightLabel("45,100", 100), RightLabel("44,900", 300));
        var mapper = NewMapper().BuildMapper(ocr, FrameWidth);

        mapper.Should().NotBeNull();
        mapper!(200).Should().BeApproximately(45000f, 0.01f);
    }

    /// <summary>Y positions closer than 20px are rejected.</summary>
    [Fact]
    public void TooCloseLabels_ReturnsNull()
    {
        var ocr = OcrWith(LeftLabel("45,100", 100), LeftLabel("44,900", 110));
        NewMapper().BuildMapper(ocr, FrameWidth).Should().BeNull();
    }

    /// <summary>Identical prices (flat axis) are rejected.</summary>
    [Fact]
    public void FlatAxis_ReturnsNull()
    {
        var ocr = OcrWith(LeftLabel("45,000", 100), LeftLabel("45,000", 300));
        NewMapper().BuildMapper(ocr, FrameWidth).Should().BeNull();
    }

    /// <summary>Mid-chart words (not on either edge) are ignored.</summary>
    [Fact]
    public void CenterWords_AreIgnored()
    {
        var ocr = OcrWith(
            new OcrWord("45,100", new Windows.Foundation.Rect(500, 100, 60, 12)),
            new OcrWord("44,900", new Windows.Foundation.Rect(500, 300, 60, 12)));
        NewMapper().BuildMapper(ocr, FrameWidth).Should().BeNull();
    }
}