namespace TradeCopilot.Core.Embeddings;

/// <summary>
/// Produces L2-normalized text embeddings.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Embedding dimensionality.
    /// </summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds text into a normalized vector of length <see cref="Dimensions"/>.
    /// </summary>
    /// <param name="text">Text to embed.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>L2-normalized embedding.</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
