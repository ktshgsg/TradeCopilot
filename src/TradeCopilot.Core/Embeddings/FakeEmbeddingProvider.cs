using System.Security.Cryptography;
using System.Text;

namespace TradeCopilot.Core.Embeddings;

/// <summary>
/// Deterministic hash-based embeddings for tests and offline fallback.
/// Same input always yields the identical byte-for-byte vector.
/// </summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>Fixed embedding dimensionality.</summary>
    public const int FakeDimensions = 384;

    /// <inheritdoc />
    public int Dimensions => FakeDimensions;

    /// <inheritdoc />
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(text);
        return Task.FromResult(Embed(text));
    }

    internal static float[] Embed(string text)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        // SplitMix64 PRNG seeded from the first 8 hash bytes; cheap and deterministic.
        ulong state = BitConverter.ToUInt64(hash, 0) | 1UL;
        var vec = new float[FakeDimensions];
        double norm = 0;
        for (int i = 0; i < vec.Length; i++)
        {
            state = unchecked(state + 0x9E3779B97F4A7C15UL);
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            // Uniform in [-1, 1] from the top 53 bits.
            double u = (z >> 11) * (1.0 / (1UL << 53));
            float v = (float)((u * 2.0) - 1.0);
            vec[i] = v;
            norm += v * v;
        }

        float inv = (float)(1.0 / Math.Sqrt(Math.Max(norm, 1e-12)));
        for (int i = 0; i < vec.Length; i++)
        {
            vec[i] *= inv;
        }

        return vec;
    }
}
