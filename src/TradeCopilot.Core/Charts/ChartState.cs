using System.Drawing;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Charts;

/// <summary>
/// A single candlestick extracted from a chart frame by classical computer vision.
/// </summary>
/// <param name="Open">Open price.</param>
/// <param name="High">High price.</param>
/// <param name="Low">Low price.</param>
/// <param name="Close">Close price.</param>
/// <param name="Index">Zero-based bar index within the visible chart area.</param>
/// <param name="IsBullish">True when close &gt;= open.</param>
/// <param name="BoundingBox">Candle body bounds in frame pixels.</param>
public sealed record Candle(
    float Open,
    float High,
    float Low,
    float Close,
    int Index,
    bool IsBullish,
    Rect BoundingBox);

/// <summary>
/// A trendline fitted to swing highs (resistance) or swing lows (support).
/// </summary>
/// <param name="Type">Resistance or support.</param>
/// <param name="Slope">Price change per pixel (y-down image coordinates).</param>
/// <param name="Intercept">Price at x = 0 in pixel space.</param>
/// <param name="TouchCount">Number of candle wicks/ bodies touching the line.</param>
/// <param name="Points">Pixel points used to fit the line.</param>
public sealed record Trendline(
    TrendlineType Type,
    float Slope,
    float Intercept,
    int TouchCount,
    IReadOnlyList<PointF> Points);

/// <summary>Kind of trendline.</summary>
public enum TrendlineType
{
    /// <summary>A resistance trendline connecting swing highs.</summary>
    Resistance,

    /// <summary>A support trendline connecting swing lows.</summary>
    Support,
}

/// <summary>
/// A horizontal support or resistance price level.
/// </summary>
/// <param name="Price">Price level.</param>
/// <param name="Type">Support or resistance.</param>
/// <param name="TouchCount">Number of times price touched this level.</param>
/// <param name="BoundingBox">Level bounds in frame pixels.</param>
public sealed record SupportResistance(
    float Price,
    LevelType Type,
    int TouchCount,
    Rect BoundingBox);

/// <summary>Kind of level.</summary>
public enum LevelType
{
    /// <summary>A support price level.</summary>
    Support,

    /// <summary>A resistance price level.</summary>
    Resistance,
}

/// <summary>
/// A chart pattern detected by classical CV (distinct from the YOLO <see
/// cref="TradeCopilot.Core.Vision.PatternDetection"/>).
/// </summary>
/// <param name="Label">Pattern name.</param>
/// <param name="Confidence">Detection confidence in [0, 1].</param>
/// <param name="BoundingBox">Pattern bounds in frame pixels.</param>
public sealed record DetectedPattern(
    string Label,
    float Confidence,
    Rect BoundingBox);

/// <summary>
/// Aggregated result of classical chart analysis: candles, trendlines, levels,
/// and patterns detected from a single frame.
/// </summary>
/// <param name="Candles">Extracted candles, sorted by index ascending.</param>
/// <param name="Trendlines">Detected trendlines, top 3 by touch count.</param>
/// <param name="Levels">Detected support/resistance levels, top 5 by touch count.</param>
/// <param name="Patterns">Detected chart patterns.</param>
/// <param name="FrameWidth">Source frame width in pixels.</param>
/// <param name="FrameHeight">Source frame height in pixels.</param>
public sealed record ChartState(
    IReadOnlyList<Candle> Candles,
    IReadOnlyList<Trendline> Trendlines,
    IReadOnlyList<SupportResistance> Levels,
    IReadOnlyList<DetectedPattern> Patterns,
    int FrameWidth,
    int FrameHeight)
{
    /// <summary>True when too few candles were detected to produce meaningful output.</summary>
    public bool IsEmpty => Candles.Count < 5;
}