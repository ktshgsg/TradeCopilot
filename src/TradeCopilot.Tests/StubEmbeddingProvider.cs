using TradeCopilot.Core.Embeddings;

namespace TradeCopilot.Tests;

/// <summary>
/// Deterministic in-memory embedding provider for cache tests: a fixed unit-scale
/// vector per known text and zeros for anything else, with explicit cosine geometry.
/// </summary>
internal sealed class StubEmbeddingProvider : IEmbeddingProvider
{
    private readonly Dictionary<string, float[]> _vectors = new(StringComparer.Ordinal)
    {
        ["bitcoin"] = [1f, 0f, 0f, 0f],
        ["btc"] = [0.96f, 0.28f, 0f, 0f],
        ["query"] = [1f, 0f, 0f, 0f],
        ["close-a"] = [0.99f, 0.1410674f, 0f, 0f],
        ["close-b"] = [0.995f, 0.0998749f, 0f, 0f],
    };

    /// <inheritdoc />
    public int Dimensions => 4;

    /// <inheritdoc />
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Task.FromResult(_vectors.TryGetValue(text, out float[]? vector)
            ? (float[])vector.Clone()
            : new float[4]);
    }
}
