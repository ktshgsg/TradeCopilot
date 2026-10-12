namespace TradeCopilot.UI.Logic.Speech;

/// <summary>
/// Microphone abstraction for the UI process. The WPF app adapts Core's
/// <c>AudioCapture</c>; tests substitute this interface.
/// </summary>
public interface IAudioRecorder : IDisposable
{
    /// <summary>True while the capture device is running.</summary>
    bool IsRecording { get; }

    /// <summary>Raised (possibly on a worker thread) with PCM16 samples for each completed utterance.</summary>
    event Action<byte[]>? UtteranceCaptured;

    /// <summary>Starts capture. Implementations must not throw when the device is busy; set <see cref="IsRecording"/> instead.</summary>
    void Start();

    /// <summary>Stops capture and releases the device. Idempotent.</summary>
    void StopRecording();
}
