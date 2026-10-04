namespace TradeCopilot.Core.Speech;

/// <summary>
/// Text-to-speech via an external <c>piper.exe</c> process, playing the generated WAV
/// through NAudio.
/// </summary>
public interface ISpeechSynthesizer
{
    /// <summary>True once both the executable and the voice model are present.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Verifies <paramref name="piperExe"/> and <paramref name="voiceModel"/> exist.
    /// A missing file leaves the synthesizer disabled and logs instructions; never throws.
    /// </summary>
    /// <param name="piperExe">Path to piper.exe.</param>
    /// <param name="voiceModel">Path to the ONNX voice model.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes once initialization has been attempted.</returns>
    Task InitializeAsync(string piperExe, string voiceModel, CancellationToken ct);

    /// <summary>
    /// Transliterates <paramref name="text"/> to Devanagari, synthesizes it, and plays
    /// the result. No-op when disabled.
    /// </summary>
    /// <param name="text">Text to speak.</param>
    /// <param name="ct">Cancellation token; cancellation stops synthesis and playback.</param>
    /// <returns>A task that completes when playback finishes or is stopped.</returns>
    Task SpeakAsync(string text, CancellationToken ct);

    /// <summary>Stops any in-flight synthesis and playback.</summary>
    /// <returns>A task that completes once stopped.</returns>
    Task StopAsync();
}
