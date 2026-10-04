namespace TradeCopilot.Core.Speech;

/// <summary>
/// Local speech recognition over 16 kHz mono PCM16. Implementations must never throw:
/// when the model is unavailable <see cref="TranscribeAsync"/> returns
/// <see cref="string.Empty"/>.
/// </summary>
public interface ISpeechRecognizer
{
    /// <summary>True once a model has been loaded successfully.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Loads the Whisper GGML model from <paramref name="modelPath"/>. A missing file or a
    /// load failure leaves the recognizer disabled; never throws.
    /// </summary>
    /// <param name="modelPath">Path to the GGML model file.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once the model is loaded or the failure is handled.</returns>
    Task InitializeAsync(string modelPath, CancellationToken ct);

    /// <summary>
    /// Transcribes 16 kHz mono PCM16 samples and returns the raw mixed-script text.
    /// Returns <see cref="string.Empty"/> when the recognizer is disabled.
    /// </summary>
    /// <param name="pcm16kMono">PCM16 mono samples at 16 kHz.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The transcript, or an empty string when unavailable.</returns>
    Task<string> TranscribeAsync(byte[] pcm16kMono, CancellationToken ct);
}
