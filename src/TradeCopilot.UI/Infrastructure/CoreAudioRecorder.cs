using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Speech;
using TradeCopilot.UI.Logic.Speech;

namespace TradeCopilot.UI.Infrastructure;

/// <summary>
/// Adapts Core's <see cref="AudioCapture"/> (16 kHz PCM16 + energy VAD) to the UI's
/// <see cref="IAudioRecorder"/> abstraction. A second WaveInEvent in this process may
/// fail while the service holds the default mic: <see cref="AudioCapture.Start"/> logs
/// and leaves <see cref="IsRecording"/> false instead of throwing, and the view model's
/// <c>ToggleRecording</c> surfaces that in the status line.
/// </summary>
public sealed class CoreAudioRecorder : IAudioRecorder
{
    private readonly AudioCapture _capture;
    private bool _disposed;

    /// <summary>Creates the adapter around a fresh <see cref="AudioCapture"/> (default mic, 16 kHz).</summary>
    /// <param name="capture">Core capture session; passed in so DI owns its lifetime.</param>
    public CoreAudioRecorder(AudioCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        _capture = capture;
        _capture.OnUtteranceEnd += Capture_OnUtteranceEnd;
    }

    /// <summary>Creates the adapter with an internally owned capture session (NullLogger).</summary>
    public CoreAudioRecorder()
        : this(new AudioCapture(NullLogger<AudioCapture>.Instance))
    {
    }

    /// <inheritdoc />
    public bool IsRecording => _capture.IsRecording;

    /// <inheritdoc />
    public event Action<byte[]>? UtteranceCaptured;

    /// <inheritdoc />
    public void Start() => _capture.Start();

    /// <inheritdoc />
    public void StopRecording() => _capture.Stop();

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capture.OnUtteranceEnd -= Capture_OnUtteranceEnd;
        _capture.Dispose();
        UtteranceCaptured = null;
    }

    private void Capture_OnUtteranceEnd(byte[] pcm) => UtteranceCaptured?.Invoke(pcm);
}
