using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Capture;

namespace TradeCopilot.Core.Charts;

/// <summary>
/// Runs the full classical CV chart-analysis pipeline on a captured frame:
/// candle extraction → trendlines → support/resistance levels → pattern detection.
/// </summary>
public interface IChartAnalyzer
{
    /// <summary>True when the analyzer is enabled and can produce output.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Analyzes a frame and returns the chart state. Returns an empty ChartState
    /// (IsEmpty = true) when the analyzer is disabled or too few candles are found.
    /// </summary>
    ChartState Analyze(FrameData frame);
}

/// <summary>
/// Default implementation of <see cref="IChartAnalyzer"/>. All detection stages are
/// deterministic and fault-tolerant: a failing stage is logged and skipped, never
/// throwing.
/// </summary>
public sealed class ChartAnalyzer : IChartAnalyzer
{
    private readonly ILogger<ChartAnalyzer> _log;

    /// <summary>Creates the analyzer.</summary>
    /// <param name="logger">Logger (defaults to null logger).</param>
    public ChartAnalyzer(ILogger<ChartAnalyzer>? logger = null)
    {
        _log = logger ?? NullLogger<ChartAnalyzer>.Instance;
        Enabled = true;
    }

    /// <inheritdoc />
    public bool Enabled { get; }

    /// <inheritdoc />
    public ChartState Analyze(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!Enabled)
        {
            return Empty(frame);
        }

        IReadOnlyList<Candle> candles;
        try
        {
            candles = CandleExtractor.Extract(frame);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Candle extraction failed; returning empty chart state.");
            return Empty(frame);
        }

        if (candles.Count < 5)
        {
            return new ChartState(
                Candles: candles,
                Trendlines: [],
                Levels: [],
                Patterns: [],
                FrameWidth: frame.Width,
                FrameHeight: frame.Height);
        }

        IReadOnlyList<Trendline> trendlines;
        try
        {
            trendlines = TrendlineDetector.Detect(candles);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Trendline detection failed.");
            trendlines = [];
        }

        IReadOnlyList<SupportResistance> levels;
        try
        {
            levels = LevelDetector.Detect(candles);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Level detection failed.");
            levels = [];
        }

        IReadOnlyList<DetectedPattern> patterns;
        try
        {
            patterns = PatternDetector.Detect(candles);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Pattern detection failed.");
            patterns = [];
        }

        return new ChartState(
            Candles: candles,
            Trendlines: trendlines,
            Levels: levels,
            Patterns: patterns,
            FrameWidth: frame.Width,
            FrameHeight: frame.Height);
    }

    private static ChartState Empty(FrameData frame) =>
        new(
            Candles: [],
            Trendlines: [],
            Levels: [],
            Patterns: [],
            FrameWidth: frame.Width,
            FrameHeight: frame.Height);
}