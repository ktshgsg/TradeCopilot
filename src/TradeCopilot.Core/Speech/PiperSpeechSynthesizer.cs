using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.Wave;

namespace TradeCopilot.Core.Speech;

/// <summary>
/// <see cref="ISpeechSynthesizer"/> that shells out to Piper, writing UTF-8 Devanagari on
/// stdin and playing the produced WAV through NAudio.
/// </summary>
/// <remarks>
/// Text is first passed through <see cref="HinglishTransliterator"/> so the Hindi voice
/// pronounces Roman-script Hinglish correctly. The synthesizer is disabled (and every
/// call is a logged no-op) when <c>piper.exe</c> or the voice model is missing. Only one
/// utterance is synthesized or played at a time.
/// </remarks>
public sealed class PiperSpeechSynthesizer : ISpeechSynthesizer, IDisposable
{
    /// <summary>Seconds to wait for piper.exe to finish before killing it.</summary>
    public const int SynthesisTimeoutSeconds = 10;

    private readonly ILogger<PiperSpeechSynthesizer> _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _processGate = new();
    private string _piperExe = string.Empty;
    private string _voiceModel = string.Empty;
    private Process? _process;
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private bool _disposed;

    /// <summary>Creates a disabled synthesizer; call <see cref="InitializeAsync"/> to enable it.</summary>
    /// <param name="logger">Optional logger.</param>
    public PiperSpeechSynthesizer(ILogger<PiperSpeechSynthesizer>? logger = null) =>
        _log = logger ?? NullLogger<PiperSpeechSynthesizer>.Instance;

    /// <inheritdoc />
    public bool Enabled { get; private set; }

    /// <inheritdoc />
    public Task InitializeAsync(string piperExe, string voiceModel, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();

        if (!File.Exists(piperExe))
        {
            _log.LogWarning(
                "piper.exe not found at {PiperExe}; speech synthesis disabled. See tools/README.md for download and setup instructions.",
                piperExe);
            return Task.CompletedTask;
        }

        if (!File.Exists(voiceModel))
        {
            _log.LogWarning(
                "Piper voice model not found at {VoiceModel}; speech synthesis disabled. See tools/README.md and models/README.md for setup instructions.",
                voiceModel);
            return Task.CompletedTask;
        }

        Enabled = true;
        _piperExe = piperExe;
        _voiceModel = voiceModel;
        _log.LogInformation("Piper TTS enabled using {PiperExe} with voice {VoiceModel}.", piperExe, voiceModel);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task SpeakAsync(string text, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!Enabled || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        string tempWav = Path.Combine(Path.GetTempPath(), string.Create(CultureInfo.InvariantCulture, $"tc-piper-{Guid.NewGuid():N}.wav"));
        try
        {
            string devanagari = HinglishTransliterator.ToDevanagari(text);
            if (!await SynthesizeAsync(devanagari, tempWav, ct).ConfigureAwait(false))
            {
                return;
            }

            ct.ThrowIfCancellationRequested();
            Play(tempWav, ct);
        }
        catch (OperationCanceledException)
        {
            _log.LogInformation("Piper speech cancelled.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "Piper speech synthesis failed.");
        }
        finally
        {
            TryDelete(tempWav);
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task StopAsync()
    {
        StopPlayback();
        lock (_processGate)
        {
            KillProcess();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs piper.exe, feeding <paramref name="devanagari"/> on stdin as UTF-8 and writing
    /// a WAV to <paramref name="outputPath"/>. Internal so tests can synthesize to a file
    /// without playback (see TradeCopilot.Tests.SpeechTests).
    /// </summary>
    /// <returns>True when the WAV was produced before the timeout or cancellation.</returns>
    internal async Task<bool> SynthesizeAsync(string devanagari, string outputPath, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _piperExe,
            Arguments = string.Create(
                CultureInfo.InvariantCulture,
                $"--model \"{_voiceModel}\" --output_file \"{outputPath}\""),
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,

            // Piper reads UTF-8 from stdin; without this the Devanagari text is mangled.
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            if (!process.Start())
            {
                _log.LogWarning("Failed to start piper.exe; skipping speech.");
                return false;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Failed to start piper.exe at {PiperExe}; skipping speech.", _piperExe);
            return false;
        }

        lock (_processGate)
        {
            _process = process;
        }

        try
        {
            await process.StandardInput.WriteAsync(devanagari.AsMemory(), ct).ConfigureAwait(false);
            process.StandardInput.Close();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(SynthesisTimeoutSeconds));

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return process.ExitCode == 0 && File.Exists(outputPath);
        }
        catch (OperationCanceledException)
        {
            KillProcess();
            if (!ct.IsCancellationRequested)
            {
                _log.LogWarning(
                    "Piper synthesis exceeded {Timeout}s and was terminated.",
                    SynthesisTimeoutSeconds);
            }

            return false;
        }
        finally
        {
            lock (_processGate)
            {
                _process = null;
            }
        }
    }

    /// <summary>Plays a WAV file through NAudio, honouring cancellation.</summary>
    private void Play(string wavPath, CancellationToken ct)
    {
        using var reader = new AudioFileReader(wavPath);
        var output = new WaveOutEvent();
        output.Init(reader);
        lock (_processGate)
        {
            _reader = reader;
            _output = output;
        }

        try
        {
            output.Play();
            WaitForPlayback(output, ct);
        }
        finally
        {
            lock (_processGate)
            {
                _output = null;
                _reader = null;
            }

            output.Stop();
            output.Dispose();
        }
    }

    /// <summary>Blocks until playback finishes or the token is cancelled.</summary>
    private static void WaitForPlayback(WaveOutEvent output, CancellationToken ct)
    {
        using var finished = new ManualResetEventSlim(false);
        void OnStopped(object? sender, StoppedEventArgs e) => finished.Set();
        output.PlaybackStopped += OnStopped;
        try
        {
            using var registration = ct.Register(() => finished.Set());
            while (!finished.IsSet)
            {
                if (output.PlaybackState == PlaybackState.Stopped)
                {
                    break;
                }

                finished.Wait(TimeSpan.FromMilliseconds(50), ct);
            }
        }
        finally
        {
            output.PlaybackStopped -= OnStopped;
        }
    }

    private void StopPlayback()
    {
        lock (_processGate)
        {
            try
            {
                _output?.Stop();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogWarning(ex, "Error while stopping audio playback.");
            }
        }
    }

    private void KillProcess()
    {
        Process? process;
        lock (_processGate)
        {
            process = _process;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Error while stopping piper.exe.");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // A leftover temp WAV is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // A leftover temp WAV is harmless.
        }
    }

    /// <summary>Stops playback and releases resources.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Enabled = false;
        StopPlayback();
        KillProcess();
        _gate.Dispose();
    }
}
