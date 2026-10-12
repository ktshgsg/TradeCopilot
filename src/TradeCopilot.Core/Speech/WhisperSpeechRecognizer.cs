using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Whisper.net;

namespace TradeCopilot.Core.Speech;

/// <summary>
/// <see cref="ISpeechRecognizer"/> backed by Whisper.net 1.9.1.
/// </summary>
/// <remarks>
/// Uses the reflection-verified 1.9.1 fluent chain
/// <c>WhisperFactory.FromPath(...).CreateBuilder().WithLanguage("hi").WithPrompt(...)
/// .WithTemperature(0f).WithThreads(n-1).WithGreedySamplingStrategy(...).Build()</c>
/// and consumes <c>ProcessAsync(float[], ct)</c>, which returns
/// <c>IAsyncEnumerable&lt;SegmentData&gt;</c>. Recognition runs on a background task and is
/// serialized, so one instance serves one request at a time. A missing model or a load
/// failure disables the recognizer instead of throwing.
/// </remarks>
public sealed class WhisperSpeechRecognizer : ISpeechRecognizer, IDisposable
{
    /// <summary>Default GGML model path, relative to the working directory.</summary>
    public const string DefaultModelPath = "models/ggml-base.bin";

    /// <summary>Where to obtain the Whisper GGML model.</summary>
    public const string ModelDownloadUrl =
        "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin";

    /// <summary>
    /// Beam size was 5; on CPU that multiplies decode cost ~5x and beam search
    /// amplifies hallucinated tokens on noisy audio. Greedy (bestOf=1) trades a
    /// little accuracy for ~5x speed, which is the right call for interactive use.
    /// </summary>
    public const int BestOf = 1;

    private const float PcmScale = 32768f;

    /// <summary>Domain priming so Whisper prefers trading vocabulary.</summary>
    private const string DomainPrompt =
        "Trading chart analysis. RSI, MACD, support, resistance, breakout, divergence, "
        + "trend, entry, exit, stop loss, target.";

    private readonly ILogger<WhisperSpeechRecognizer> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private bool _loggedDisabled;
    private bool _disposed;

    /// <summary>Creates a disabled recognizer; call <see cref="InitializeAsync"/> to load a model.</summary>
    /// <param name="logger">Optional logger.</param>
    public WhisperSpeechRecognizer(ILogger<WhisperSpeechRecognizer>? logger = null) =>
        _log = logger ?? NullLogger<WhisperSpeechRecognizer>.Instance;

    /// <inheritdoc />
    public bool Enabled => _processor is not null;

    /// <inheritdoc />
    public Task InitializeAsync(string modelPath, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        if (_processor is not null)
        {
            return Task.CompletedTask;
        }

        if (!File.Exists(modelPath))
        {
            _log.LogWarning(
                "Whisper model not found at {ModelPath}; speech recognition disabled. Download it from {Url} (see models/README.md).",
                modelPath,
                ModelDownloadUrl);
            return Task.CompletedTask;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _factory = WhisperFactory.FromPath(modelPath);
            _processor = _factory.CreateBuilder()
                .WithLanguage("hi")
                .WithPrompt(DomainPrompt)
                .WithTemperature(0.0f)
                .WithThreads(Math.Max(1, Environment.ProcessorCount - 1))
                .WithGreedySamplingStrategy(b => b.WithBestOf(BestOf))
                .Build();
            stopwatch.Stop();
            _log.LogInformation(
                "Whisper model loaded from {ModelPath} in {ElapsedMs} ms.",
                modelPath,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "Failed to load Whisper model from {ModelPath}; recognition disabled.", modelPath);
            DisposeModel();
        }

        return Task.CompletedTask;
    }
    /// <inheritdoc />
    public async Task<string> TranscribeAsync(byte[] pcm16kMono, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        WhisperProcessor? processor = _processor;
        if (processor is null)
        {
            LogDisabledOnce();
            return string.Empty;
        }

        if (pcm16kMono is null || pcm16kMono.Length < 2)
        {
            return string.Empty;
        }

        float[] samples = Pcm16ToFloat(pcm16kMono);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => RunAsync(processor, samples, ct), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "Whisper transcription failed.");
            return string.Empty;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<string> RunAsync(WhisperProcessor processor, float[] samples, CancellationToken ct)
    {
        var builder = new StringBuilder();
        await foreach (SegmentData segment in processor
            .ProcessAsync(samples, ct)
            .WithCancellation(ct)
            .ConfigureAwait(false))
        {
            builder.Append(segment.Text);
        }

        return CollapseWhitespace(builder.ToString());
    }

    /// <summary>Converts PCM16 samples to normalized floats in [-1, 1].</summary>
    private static float[] Pcm16ToFloat(byte[] pcm)
    {
        int count = pcm.Length / 2;
        var samples = new float[count];
        for (int i = 0; i < count; i++)
        {
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / PcmScale;
        }

        return samples;
    }

    /// <summary>Trims and collapses internal whitespace runs to single spaces.</summary>
    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        bool pendingSpace = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private void LogDisabledOnce()
    {
        if (!_loggedDisabled)
        {
            _loggedDisabled = true;
            _log.LogWarning("Speech recognition unavailable: no Whisper model is loaded; returning empty transcript.");
        }
    }

    /// <summary>Releases the processor and factory.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeModel();
        _gate.Dispose();
    }

    private void DisposeModel()
    {
        _processor?.Dispose();
        _processor = null;
        _factory?.Dispose();
        _factory = null;
    }
}


