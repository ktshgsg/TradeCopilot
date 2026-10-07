using System.Diagnostics;
using System.Reflection;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Privacy;
using TradeCopilot.Core.Processing;
using TradeCopilot.Core.Speech;

namespace TradeCopilot.Service;

/// <summary>
/// Initializes every engine, warms the query path, wires capture into the frame
/// processor, and starts capturing.
/// </summary>
/// <remarks>
/// Engine initialization runs in parallel and each engine is isolated: a missing model
/// disables that engine and is logged, but never aborts startup. The final
/// <c>"Ready"</c> log line is the signal scripts and installers grep for.
/// </remarks>
public sealed class StartupHostedService : IHostedService
{
    private readonly ISemanticCache _cache;
    private readonly ILlmEngine _llm;
    private readonly ISpeechRecognizer _asr;
    private readonly ISpeechSynthesizer _tts;
    private readonly AgentState _state;
    private readonly AgentOrchestrator _orchestrator;
    private readonly IScreenCapture _capture;
    private readonly AudioCapture _audio;
    private readonly FrameProcessor _frameProcessor;
    private readonly AgentOptions _opts;
    private readonly IWindowFilter _windowFilter;
    private readonly IAuditSink _audit;
    private readonly PrivacyOptions _privacy;
    private readonly PrivacyGateCounters _counters;
    private readonly ILogger<StartupHostedService> _log;

    private EventHandler<FrameData>? _frameHandler;
    private EventHandler<ProcessedFrame>? _processedHandler;
    private bool _started;

    /// <summary>Creates the startup service.</summary>
    /// <param name="cache">Semantic cache.</param>
    /// <param name="llm">LLM engine.</param>
    /// <param name="asr">Speech recognizer.</param>
    /// <param name="tts">Speech synthesizer.</param>
    /// <param name="state">Agent state.</param>
    /// <param name="orchestrator">Query orchestrator.</param>
    /// <param name="capture">Screen capture.</param>
    /// <param name="audio">Microphone capture, disposed on shutdown.</param>
    /// <param name="frameProcessor">Frame processing pipeline.</param>
    /// <param name="opts">Service options.</param>
    /// <param name="windowFilter">Foreground-window exclusion filter.</param>
    /// <param name="audit">Content-free audit sink.</param>
    /// <param name="privacy">Privacy gate options.</param>
    /// <param name="counters">Shared privacy exclusion counters.</param>
    /// <param name="log">Logger.</param>
    public StartupHostedService(
        ISemanticCache cache,
        ILlmEngine llm,
        ISpeechRecognizer asr,
        ISpeechSynthesizer tts,
        AgentState state,
        AgentOrchestrator orchestrator,
        IScreenCapture capture,
        AudioCapture audio,
        FrameProcessor frameProcessor,
        AgentOptions opts,
        ILogger<StartupHostedService> log,
        IWindowFilter? windowFilter = null,
        IAuditSink? audit = null,
        PrivacyOptions? privacy = null,
        PrivacyGateCounters? counters = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(asr);
        ArgumentNullException.ThrowIfNull(tts);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(orchestrator);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(frameProcessor);
        ArgumentNullException.ThrowIfNull(opts);
        ArgumentNullException.ThrowIfNull(log);
        _cache = cache;
        _llm = llm;
        _asr = asr;
        _tts = tts;
        _state = state;
        _orchestrator = orchestrator;
        _capture = capture;
        _audio = audio;
        _frameProcessor = frameProcessor;
        _opts = opts;
        _log = log;
        _windowFilter = windowFilter ?? new WindowFilter();
        _audit = audit ?? NoopAuditSink.Instance;
        _privacy = privacy ?? opts.Privacy;
        _counters = counters ?? new PrivacyGateCounters();
    }

    /// <summary>Service version reported in the startup log.</summary>
    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? PipeServer.ServiceVersion;

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("TradeCopilot starting, version {Version}", Version);

        await InitializeEnginesAsync(cancellationToken).ConfigureAwait(false);
        await WarmUpAsync(cancellationToken).ConfigureAwait(false);
        WirePipeline();
        await _capture.StartAsync(_opts.Capture.Region(), _opts.Capture.Fps, cancellationToken)
            .ConfigureAwait(false);
        _started = true;

        _log.LogInformation("Ready");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _capture.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Error stopping capture during shutdown.");
        }

        UnwirePipeline();

        // Dispose in reverse registration order. The engine interfaces are not
        // IDisposable, so each implementation is pattern-matched; FrameProcessor is only
        // IAsyncDisposable.
        await DisposeFrameProcessorAsync().ConfigureAwait(false);
        DisposeIfDisposable(_tts);
        DisposeIfDisposable(_asr);
        DisposeIfDisposable(_llm);
        DisposeIfDisposable(_audio);
        DisposeIfDisposable(_capture);

        _started = false;
        _log.LogInformation("Stopped");
    }

    /// <summary>True once capture has been started and not yet stopped.</summary>
    public bool Started => _started;

    /// <summary>Frames dropped before OCR because the window title was excluded.</summary>
    public long FramesExcludedByWindow => _counters.ExcludedByWindow;

    /// <summary>Frames dropped after OCR because the content was sensitive.</summary>
    public long FramesExcludedBySensitive => _counters.ExcludedBySensitive;


    /// <summary>
    /// Initializes every engine in parallel. Each engine is isolated so a missing model
    /// disables only that engine.
    /// </summary>
    private async Task InitializeEnginesAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        await Task.WhenAll(
            MeasureAsync("Cache", null, async () =>
            {
                EnsureDirectoryExists(_opts.Cache.DbPath);
                await _cache.InitializeAsync(_opts.Cache.DbPath, ct).ConfigureAwait(false);
            }),
            MeasureAsync("LLM", () => _llm.Enabled, async () =>
                await _llm.InitializeAsync(_opts.Models.LlmPath, ct).ConfigureAwait(false)),
            MeasureAsync("ASR", () => _asr.Enabled, async () =>
                await _asr.InitializeAsync(_opts.Models.WhisperPath, ct).ConfigureAwait(false)),
            MeasureAsync("TTS", () => _tts.Enabled, async () =>
            {
                await _tts.InitializeAsync(_opts.Models.PiperExePath, _opts.Models.PiperVoicePath, ct)
                    .ConfigureAwait(false);
            })).ConfigureAwait(false);

        stopwatch.Stop();
        _log.LogInformation(
            "Engine init finished in {ElapsedMs}ms (cache ok, llm enabled={LlmEnabled}, asr enabled={AsrEnabled}, tts enabled={TtsEnabled}).",
            stopwatch.ElapsedMilliseconds,
            _llm.Enabled,
            _asr.Enabled,
            _tts.Enabled);
    }

    /// <summary>
    /// Runs one engine initialization, logging success or failure without throwing.
    /// <paramref name="isEnabled"/> reports the engine's real enabled state (null when the
    /// engine exposes no such flag), so a missing model logs <c>enabled=false</c>.
    /// </summary>
    private async Task MeasureAsync(string name, Func<bool>? isEnabled, Func<Task> init)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await init().ConfigureAwait(false);
            stopwatch.Stop();
            _log.LogInformation(
                "{Name} init: enabled={Enabled} in {ElapsedMs}ms",
                name,
                isEnabled?.Invoke() ?? true,
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            _log.LogWarning("{Name} init: cancelled.", name);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stopwatch.Stop();
            _log.LogWarning(
                ex,
                "{Name} init: enabled=false in {ElapsedMs}ms",
                name,
                stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>Creates the directory holding <paramref name="filePath"/> when missing.</summary>
    private static void EnsureDirectoryExists(string filePath)
    {
        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Sends a throwaway query through the orchestrator so JIT, the compiled Hinglish
    /// regex, and the first SQLite query plan are paid at startup rather than by the
    /// first real user. A failure here must not block startup.
    /// </summary>
    private async Task WarmUpAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            _ = await _orchestrator.AnswerAsync("warmup", ct).ConfigureAwait(false);
            stopwatch.Stop();
            _log.LogInformation("Warm-up completed in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stopwatch.Stop();
            _log.LogWarning(ex, "Warm-up failed after {ElapsedMs}ms; continuing.", stopwatch.ElapsedMilliseconds);
        }
    }

    private void WirePipeline()
    {
        _frameHandler = (_, frame) =>
        {
            try
            {
                if (_windowFilter.IsExcluded(_privacy.ExclusionPatterns))
                {
                    _counters.IncrementWindow();
                    _audit.Write("SensitiveSkipped", "reason=window_title");
                    _log.LogDebug("Frame dropped: foreground window is excluded.");
                    return; // do NOT call frameProcessor.Post
                }

                _frameProcessor.Post(frame);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogError(ex, "Gate 1 failed; dropping frame.");
            }
        };
        _processedHandler = (_, processed) =>
        {
            try
            {
                // Gate 2 MUST run before state.Update: once sensitive OCR reaches
                // AgentState it will be sent to the LLM on the next query.
                if (_privacy.PauseOnSensitive)
                {
                    string title = _windowFilter.GetForegroundWindowTitle();
                    if (SensitiveDetector.IsSensitive(processed.Ocr.FullText, title))
                    {
                        _counters.IncrementSensitive();
                        _audit.Write("SensitiveSkipped", "reason=ocr_content");
                        return; // do NOT call state.Update
                    }
                }

                _state.Update(processed.Ocr, processed.Patterns);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogError(ex, "Gate 2 failed; skipping frame.");
            }
        };
        _capture.OnFrameCaptured += _frameHandler;
        _frameProcessor.OnProcessed += _processedHandler;
    }

    private void UnwirePipeline()
    {
        if (_frameHandler is not null)
        {
            _capture.OnFrameCaptured -= _frameHandler;
            _frameHandler = null;
        }

        if (_processedHandler is not null)
        {
            _frameProcessor.OnProcessed -= _processedHandler;
            _processedHandler = null;
        }
    }

    private async Task DisposeFrameProcessorAsync()
    {
        try
        {
            await _frameProcessor.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Error disposing {Engine} during shutdown.", nameof(FrameProcessor));
        }
    }

    private void DisposeIfDisposable(object? component)
    {
        if (component is not IDisposable disposable)
        {
            return;
        }

        try
        {
            disposable.Dispose();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Error disposing {Engine} during shutdown.", component.GetType().Name);
        }
    }
}
