using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Processing;

/// <summary>
/// Bounded single-consumer pipeline: frames are gated by dHash, then OCR and pattern
/// detection run in parallel and the merged result is raised via <see cref="OnProcessed"/>.
/// </summary>
public sealed class FrameProcessor : IAsyncDisposable
{
    private readonly Channel<FrameData> _channel;
    private readonly FrameGate _gate;
    private readonly IOcrEngine _ocr;
    private readonly IChartPatternDetector _patternDetector;
    private readonly ILogger<FrameProcessor> _log;
    private const int ChannelCapacity = 5;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _consumer;
    private bool _disposed;
    private long _droppedBacking;

    /// <summary>Creates a processor with a DropOldest channel of capacity 5.</summary>
    /// <param name="gate">Duplicate-frame gate.</param>
    /// <param name="ocr">OCR engine.</param>
    /// <param name="patternDetector">Pattern detector (defaults to disabled).</param>
    /// <param name="logger">Logger (defaults to null logger).</param>
    public FrameProcessor(
        FrameGate gate,
        IOcrEngine ocr,
        IChartPatternDetector? patternDetector = null,
        ILogger<FrameProcessor>? logger = null)
    {
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _ocr = ocr ?? throw new ArgumentNullException(nameof(ocr));
        _patternDetector = patternDetector ?? DisabledChartPatternDetector.Instance;
        _log = logger ?? NullLogger<FrameProcessor>.Instance;
        _channel = Channel.CreateBounded<FrameData>(new BoundedChannelOptions(ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>Raised for each accepted frame after OCR + pattern detection complete.</summary>
    public event EventHandler<ProcessedFrame>? OnProcessed;

    /// <summary>Number of frames dropped because the channel was full (DropOldest).</summary>
    public long DroppedCount => Interlocked.Read(ref _droppedBacking);

    /// <summary>Enqueues a frame; drops the oldest when the channel is full.</summary>
    /// <param name="frame">Frame to process.</param>
    public void Post(FrameData frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // With DropOldest, TryWrite only fails after completion; count a drop when the
        // channel is already full, since the oldest queued frame will be discarded.
        if (_channel.Reader.Count >= ChannelCapacity)
        {
            Interlocked.Increment(ref _droppedBacking);
        }

        if (!_channel.Writer.TryWrite(frame))
        {
            Interlocked.Increment(ref _droppedBacking);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _channel.Writer.Complete();
        _cts.Cancel();
        try
        {
            await _consumer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    private async Task ConsumeAsync()
    {
        try
        {
            await foreach (FrameData frame in _channel.Reader.ReadAllAsync(_cts.Token).ConfigureAwait(false))
            {
                bool accepted;
                try
                {
                    accepted = _gate.ShouldProcess(frame);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Frame gate failed; skipping frame.");
                    continue;
                }

                if (!accepted)
                {
                    continue;
                }

                Task<OcrResult> ocrTask;
                Task<IReadOnlyList<PatternDetection>> patternTask;
                try
                {
                    ocrTask = _ocr.RecognizeAsync(frame, _cts.Token);
                    patternTask = _patternDetector.DetectAsync(frame, _cts.Token);
                    await Task.WhenAll(ocrTask, patternTask).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Frame processing failed; skipping frame.");
                    continue;
                }

                OnProcessed?.Invoke(
                    this,
                    new ProcessedFrame(frame, await ocrTask.ConfigureAwait(false), await patternTask.ConfigureAwait(false), DateTime.UtcNow));
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
