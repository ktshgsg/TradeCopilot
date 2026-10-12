using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace TradeCopilot.Core.Capture;

/// <summary>
/// Desktop Duplication (DXGI) screen capturer. The capture loop runs on a dedicated
/// background thread; each acquired frame is copied to CPU memory and raised via
/// <see cref="OnFrameCaptured"/>. Buffer ownership transfers to the subscriber (see
/// <see cref="FrameData"/>); subscribers must copy if they retain data past the handler.
/// </summary>
public sealed partial class DxgiScreenCapture : IDisposable
{
    private const int DefaultTargetFps = 5;
    private const int AcquireTimeoutMs = 100;
    private const int StopJoinTimeoutMs = 5000;

    private readonly object _sync = new();
    private readonly ILogger<DxgiScreenCapture> _log;
    private readonly CaptureRegion _region;
    private readonly int _targetFps;

    private ID3D11Device? _device;
    private IDXGIOutputDuplication? _duplication;
    private ID3D11Texture2D? _staging;
    private int _desktopWidth;
    private int _desktopHeight;

    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private long _sequence;
    private long _emptyFrameSkips;

    // True from each DuplicateOutput until the first acquire completes: that very first
    // AcquireNextFrame can report AccumulatedFrames=0 with an empty texture (observed on
    // Intel HD under CPU contention). Later acc=0 frames are legitimate "desktop unchanged"
    // notifications carrying the last real frame's content and must be published.
    private volatile bool _firstFrameAfterInit = true;
    private bool _initialized;
    private bool _disposed;

    private volatile bool _capturing;

    /// <summary>True while the capture thread is running.</summary>
    public bool IsCapturing => _capturing;

    /// <summary>Empty first-acquire frames (AccumulatedFrames=0) skipped since construction.</summary>
    public long EmptyFrameSkips => Interlocked.Read(ref _emptyFrameSkips);

    /// <summary>Which D3D11 driver the device was created with (Hardware, or WARP fallback).</summary>
    public string DriverKind { get; private set; } = "Unknown";

    /// <summary>Raised on the capture thread for each acquired frame.</summary>
    public event EventHandler<FrameData>? OnFrameCaptured;

    /// <summary>
    /// Creates a capturer for the given desktop region (or the full primary output when null).
    /// </summary>
    /// <param name="region">Region to crop, or null for the full screen.</param>
    /// <param name="targetFps">Frame-rate cap; the loop sleeps the remainder of 1000/targetFps ms.</param>
    /// <param name="logger">Logger (defaults to null logger).</param>
    public DxgiScreenCapture(CaptureRegion? region = null, int targetFps = DefaultTargetFps, ILogger<DxgiScreenCapture>? logger = null)
    {
        if (targetFps <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFps), "Target FPS must be positive.");
        }

        _region = region ?? new CaptureRegion(0, 0, 0, 0);
        _targetFps = targetFps;
        _log = logger ?? NullLogger<DxgiScreenCapture>.Instance;
    }

    /// <summary>Creates the D3D11 device, output duplication, and staging texture.</summary>
    /// <param name="ct">Cancellation token.</param>
    public Task InitializeAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_initialized)
            {
                return Task.CompletedTask;
            }

            InitializeCore();
            _initialized = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Initializes and launches the dedicated capture thread (background, above-normal priority).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await InitializeAsync(ct).ConfigureAwait(false);
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_thread?.IsAlive == true)
            {
                return;
            }

            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _thread = new Thread(() => CaptureLoop(_cts.Token))
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "DxgiScreenCapture",
            };
            _thread.Start();
        }
    }

    /// <summary>Signals cancellation and joins the capture thread (5s timeout).</summary>
    /// <param name="ct">Cancellation token (reserved; join always waits up to 5s).</param>
    public Task StopAsync(CancellationToken ct = default)
    {
        Thread? thread;
        CancellationTokenSource? cts;
        lock (_sync)
        {
            thread = _thread;
            cts = _cts;
            _thread = null;
            _cts = null;
        }

        if (thread is null)
        {
            cts?.Dispose();
            return Task.CompletedTask;
        }

        try
        {
            cts?.Cancel();
            if (!thread.Join(StopJoinTimeoutMs))
            {
                _log.LogWarning("Capture thread did not stop within {TimeoutMs} ms.", StopJoinTimeoutMs);
            }
        }
        finally
        {
            cts?.Dispose();
        }

        _log.LogInformation(
            "Capture stopped; {EmptyFrameSkips} empty first-acquire frame(s) skipped.",
            Interlocked.Read(ref _emptyFrameSkips));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Error stopping capture during dispose.");
        }

        lock (_sync)
        {
            _staging?.Dispose();
            _staging = null;
            _duplication?.Dispose();
            _duplication = null;
            _device?.Dispose();
            _device = null;
            _initialized = false;
        }
    }
}
