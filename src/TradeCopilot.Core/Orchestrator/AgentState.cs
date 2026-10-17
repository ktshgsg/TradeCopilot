using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Charts;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Orchestrator;

/// <summary>
/// Immutable view of the agent's current chart context.
/// </summary>
/// <param name="Ocr">Latest OCR result, or null when no frame has been processed.</param>
/// <param name="Patterns">Patterns detected in the latest frame.</param>
/// <param name="ChartState">Classical CV chart analysis result, or empty when no usable frame.</param>
/// <param name="Frame">Latest captured frame (owned by the state, never pooled), or null.</param>
/// <param name="ContextHash">Chart fingerprint, or an empty string when there is no data.</param>
/// <param name="LastUpdate">When the state was last updated (UTC).</param>
/// <param name="FramesProcessed">Number of frames folded into the state.</param>
public sealed record AgentSnapshot(
    OcrResult? Ocr,
    IReadOnlyList<PatternDetection> Patterns,
    ChartState ChartState,
    FrameData? Frame,
    string ContextHash,
    DateTimeOffset LastUpdate,
    long FramesProcessed);

/// <summary>
/// Holds the latest OCR text and detected patterns, and derives the chart-context hash
/// used to key the semantic cache.
/// </summary>
/// <remarks>
/// Updated on every captured frame, so both <see cref="Update(OcrResult, IReadOnlyList{PatternDetection}, FrameData, ChartState)"/> and <see cref="Snapshot"/>
/// are lock-guarded and allocation-light. <see cref="Snapshot"/> always hands back copies,
/// never the internal pattern array, so callers cannot mutate shared state.
/// </remarks>
public sealed class AgentState
{
    private readonly object _gate = new();
    private OcrResult? _ocr;
    private PatternDetection[] _patterns = [];
    private ChartState _chartState = new([], [], [], [], 0, 0);
    private FrameData? _frame;
    private string _contextHash = string.Empty;
    private DateTimeOffset _lastUpdate;
    private long _framesProcessed;

    /// <summary>Returns an immutable copy of the current state.</summary>
    public AgentSnapshot Snapshot()
    {
        lock (_gate)
        {
            return new AgentSnapshot(
                _ocr,
                _patterns.ToArray(),
                _chartState,
                _frame,
                _contextHash,
                _lastUpdate,
                _framesProcessed);
        }
    }

    /// <summary>
    /// Replaces the OCR result and patterns (clearing the frame and chart state),
    /// recomputes <see cref="ChartContextHash"/> when OCR data is present, and increments
    /// <see cref="AgentSnapshot.FramesProcessed"/>. A null <paramref name="ocr"/> clears the
    /// context hash.
    /// </summary>
    /// <param name="ocr">Latest OCR result, or null to clear it.</param>
    /// <param name="patterns">Patterns detected in the latest frame.</param>
    public void Update(OcrResult? ocr, IReadOnlyList<PatternDetection> patterns) =>
        Update(ocr, patterns, null, new ChartState([], [], [], [], 0, 0));

    /// <summary>
    /// Replaces the OCR result, patterns, chart state, and latest frame, recomputes
    /// <see cref="ChartContextHash"/> when OCR data is present, and increments
    /// <see cref="AgentSnapshot.FramesProcessed"/>. A null <paramref name="ocr"/> clears the
    /// context hash.
    /// </summary>
    /// <param name="ocr">Latest OCR result, or null to clear it.</param>
    /// <param name="patterns">Patterns detected in the latest frame.</param>
    /// <param name="frame">
    /// Latest captured frame, stored by reference (it is never pooled or mutated after
    /// capture) or null to clear it.
    /// </param>
    /// <param name="chartState">Classical CV chart analysis result, or empty when unavailable.</param>
    public void Update(
        OcrResult? ocr,
        IReadOnlyList<PatternDetection> patterns,
        FrameData? frame,
        ChartState? chartState = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        lock (_gate)
        {
            _ocr = ocr;

            // Always take our own copy so a caller mutating its list cannot corrupt us.
            _patterns = patterns.ToArray();
            _frame = frame;
            _chartState = chartState ?? new ChartState([], [], [], [], 0, 0);
            _contextHash = ocr is null ? string.Empty : ChartContextHash.Compute(ocr, _patterns);
            _lastUpdate = DateTimeOffset.UtcNow;
            _framesProcessed++;
        }
    }

    /// <summary>Clears OCR, patterns, chart state, the frame, the context hash, and the frame counter.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _ocr = null;
            _patterns = [];
            _chartState = new ChartState([], [], [], [], 0, 0);
            _frame = null;
            _contextHash = string.Empty;
            _lastUpdate = DateTimeOffset.UtcNow;
            _framesProcessed = 0;
        }
    }
}
