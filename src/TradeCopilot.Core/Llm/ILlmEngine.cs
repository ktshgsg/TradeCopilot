namespace TradeCopilot.Core.Llm;

/// <summary>
/// Local LLM abstraction. Implementations must not throw when the model is
/// unavailable: calls then return the "Model not available." sentinel.
/// </summary>
public interface ILlmEngine
{
    /// <summary>True once a model has been loaded successfully.</summary>
    bool Enabled { get; }

    /// <summary>
    /// Loads the GGUF model from <paramref name="modelPath"/>. A missing file or a
    /// load failure leaves the engine disabled (logged, never thrown).
    /// </summary>
    /// <param name="modelPath">Path to the GGUF file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeAsync(string modelPath, CancellationToken ct);

    /// <summary>Streams the completion token by token.</summary>
    /// <param name="system">System prompt.</param>
    /// <param name="user">User prompt.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<string> StreamAsync(string system, string user, CancellationToken ct);

    /// <summary>Returns the full completion as one string.</summary>
    /// <param name="system">System prompt.</param>
    /// <param name="user">User prompt.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<string> CompleteAsync(string system, string user, CancellationToken ct);
}
