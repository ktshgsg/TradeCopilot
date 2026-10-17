using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Ocr;

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
    ChartState Analyze(FrameData frame, CaptureRegion region, OcrResult ocr);
}

/// <summary>
/// Default implementation of <see cref="IChartAnalyzer"/>. All detection stages are
/// deterministic and fault-tolerant: a failing stage is logged and skipped, never
/// throwing.
/// </summary>
public sealed class ChartAnalyzer : IChartAnalyzer
{
    private readonly ILogger<ChartAnalyzer> _log;
    private readonly PriceAxisMapper _priceMapper;

    /// <summary>Creates the analyzer.</summary>
    /// <param name="priceMapper">Maps pixel Y to real price using OCR axis labels.</param>
    /// <param name="logger">Logger (defaults to null logger).</param>
    public ChartAnalyzer(PriceAxisMapper priceMapper, ILogger<ChartAnalyzer>? logger = null)
    {
        _priceMapper = priceMapper;
        _log = logger ?? NullLogger<ChartAnalyzer>.Instance;
        Enabled = true;
    }

    /// <inheritdoc />
    public bool Enabled { get; }

    /// <inheritdoc />
    public ChartState Analyze(FrameData frame, CaptureRegion region, OcrResult ocr)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(ocr);

        if (!Enabled)
        {
            return Empty(frame);
        }

        IReadOnlyList<Candle> rawCandles;
        try
        {
            rawCandles = CandleExtractor.Extract(frame);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Candle extraction failed; returning empty chart state.");
            return Empty(frame);
        }

        if (rawCandles.Count < 5)
        {
            return new ChartState(
                Candles: rawCandles,
                Trendlines: [],
                Levels: [],
                Patterns: [],
                FrameWidth: frame.Width,
                FrameHeight: frame.Height,
                PricesAreMapped: false);
        }

        // Step 2a: Build price axis mapper from OCR labels.
        Func<float, float>? mapper = _priceMapper.BuildMapper(ocr, region.Width);

        // Step 2b: Map candles if the axis was detected.
        IReadOnlyList<Candle> candles;
        bool pricesAreMapped;
        if (mapper is not null)
        {
            pricesAreMapped = true;
            candles = rawCandles.Select(c => new Candle(
                Open: mapper(c.Open),
                High: mapper(c.High),
                Low: mapper(c.Low),
                Close: mapper(c.Close),
                Index: c.Index,
                IsBullish: c.IsBullish,
                BoundingBox: c.BoundingBox,
                IsPriceMapped: true)).ToList();
        }
        else
        {
            pricesAreMapped = false;
            // Keep pixel values and mark as unmapped.
            candles = rawCandles.Select(c => new Candle(
                Open: c.Open,
                High: c.High,
                Low: c.Low,
                Close: c.Close,
                Index: c.Index,
                IsBullish: c.IsBullish,
                BoundingBox: c.BoundingBox,
                IsPriceMapped: false)).ToList();
        }

        IReadOnlyList<Trendline> trendlines;
        try
        {
            // Trendlines need mapped prices for meaningful slope analysis.
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
            // Levels are detected on the mapped (or raw) candles.
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
            // Patterns use fixed pixel thresholds and y-down comparisons, so they
            // always run on the raw pixel-space candles.
            patterns = PatternDetector.Detect(rawCandles);
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
            FrameHeight: frame.Height,
            PricesAreMapped: pricesAreMapped);
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