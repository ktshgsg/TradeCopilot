using FluentAssertions;
using TradeCopilot.Core.Embeddings;

namespace TradeCopilot.Tests;

/// <summary>
/// Embedding tests: deterministic fake embeddings and the ONNX provider's documented
/// fallback behavior when model files are missing or the tokenizer is unsupported.
/// </summary>
public sealed class EmbeddingTests
{
    /// <summary>Fake embeddings are deterministic, 384-dim, and L2-normalized.</summary>
    [Fact]
    public async Task FakeEmbeddings_AreDeterministic_AndUnitLength()
    {
        var provider = new FakeEmbeddingProvider();
        float[] a = await provider.EmbedAsync("what is a bull flag?");
        float[] b = await provider.EmbedAsync("what is a bull flag?");
        float[] c = await provider.EmbedAsync("what is a bear flag?");

        provider.Dimensions.Should().Be(FakeEmbeddingProvider.FakeDimensions);
        a.Should().HaveCount(384);
        a.Should().Equal(b);
        a.Should().NotEqual(c);
        float norm = MathF.Sqrt(a.Sum(v => v * v));
        norm.Should().BeApproximately(1f, 1e-4f);
    }

    /// <summary>Missing model files select the fake fallback with identical output.</summary>
    [Fact]
    public async Task OnnxEmbeddingProvider_WithoutModelFiles_UsesFakeFallback()
    {
        using var provider = new OnnxEmbeddingProvider(
            modelPath: MissingPath(),
            tokenizerPath: MissingPath());

        provider.UsesFallback.Should().BeTrue();
        provider.Dimensions.Should().Be(FakeEmbeddingProvider.FakeDimensions);
        float[] viaProvider = await provider.EmbedAsync("btc support levels");
        float[] viaFake = await new FakeEmbeddingProvider().EmbedAsync("btc support levels");
        viaProvider.Should().Equal(viaFake);
    }

    /// <summary>Files present is not enough: the unsupported HF tokenizer keeps the fallback.</summary>
    [Fact]
    public async Task OnnxEmbeddingProvider_WithFilesPresentButUnsupportedTokenizer_StillFallsBack()
    {
        string model = Path.GetTempFileName();
        string tokenizer = Path.GetTempFileName();
        try
        {
            using var provider = new OnnxEmbeddingProvider(modelPath: model, tokenizerPath: tokenizer);
            provider.UsesFallback.Should().BeTrue();
            float[] vector = await provider.EmbedAsync("x");
            vector.Should().HaveCount(FakeEmbeddingProvider.FakeDimensions);
        }
        finally
        {
            File.Delete(model);
            File.Delete(tokenizer);
        }
    }

    private static string MissingPath() => Path.Combine(Path.GetTempPath(), $"tc-missing-{Guid.NewGuid():N}");
}
