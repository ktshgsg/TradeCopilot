using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;

namespace TradeCopilot.Core.Vision;

/// <summary>
/// YOLOv8 chart-pattern detector. Input RGB float32[1,3,640,640] (letterboxed);
/// supports both [1, 4+classes, N] and [1, N, 4+classes] outputs. Disabled
/// (empty results) when the model file is missing or fails to load.
/// </summary>
public sealed partial class YoloChartPatternDetector : IChartPatternDetector, IDisposable
{
    /// <summary>Model input width/height in pixels.</summary>
    public const int InputSize = 640;

    /// <summary>Default model path, relative to the working directory.</summary>
    public const string DefaultModelPath = "models/chart_patterns.onnx";

    /// <summary>Minimum class score to keep a detection.</summary>
    public const float ConfidenceThreshold = 0.35f;

    /// <summary>IoU above which same-class overlapping boxes are suppressed.</summary>
    public const float NmsIoU = 0.45f;

    private static readonly string[] ClassLabels =
    [
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
        PatternLabels.ResistanceBreak,
    ];

    private readonly ILogger<YoloChartPatternDetector> _log;
    private readonly InferenceSession? _session;
    private readonly float[] _inputBuffer = new float[3 * InputSize * InputSize];
    private readonly int[] _inputShape = [1, 3, InputSize, InputSize];
    private readonly string _inputName;
    private readonly string _outputName;
    private readonly int _classCount;
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// Creates a detector, loading the ONNX model when present.
    /// </summary>
    /// <param name="logger">Logger (defaults to null logger).</param>
    /// <param name="modelPath">Path to the chart-patterns ONNX model.</param>
    public YoloChartPatternDetector(ILogger<YoloChartPatternDetector>? logger = null, string modelPath = DefaultModelPath)
    {
        _log = logger ?? NullLogger<YoloChartPatternDetector>.Instance;
        _inputName = "images";
        _outputName = "output0";
        _classCount = ClassLabels.Length;
        if (!File.Exists(modelPath))
        {
            _log.LogWarning("Chart pattern model not found at {Path}; detection disabled.", modelPath);
            Enabled = false;
            return;
        }

        try
        {
            var options = new SessionOptions
            {
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount - 1),
            };
            _session = new InferenceSession(modelPath, options);
            _inputName = _session.InputMetadata.Keys.FirstOrDefault() ?? _inputName;
            _outputName = _session.OutputMetadata.Keys.FirstOrDefault() ?? _outputName;
            _classCount = OutputClassCount(_session.OutputMetadata[_outputName]);
            Enabled = true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to load chart pattern model at {Path}; detection disabled.", modelPath);
            _session?.Dispose();
            _session = null;
            Enabled = false;
        }
    }
}
