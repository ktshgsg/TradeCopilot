using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TradeCopilot.Core.Embeddings;

/// <summary>
/// Embedding provider backed by <c>models/embedding_model.onnx</c> plus a HuggingFace
/// <c>tokenizer.json</c>. Falls back to <see cref="FakeEmbeddingProvider"/> when the
/// model files are absent or the tokenizer format is unsupported.
/// </summary>
/// <remarks>
/// Microsoft.ML.Tokenizers 2.0.0 exposes no factory that loads a generic HuggingFace
/// <c>tokenizer.json</c> (verified against Microsoft.ML.Tokenizers.xml: only
/// Bert/WordPiece, BPE-from-vocab+merges, CodeGen, Llama/SentencePiece-from-model-stream,
/// Tiktoken, and EnglishRoberta factories exist). Hand-rolling a BPE tokenizer is
/// explicitly out of scope, so the ONNX path stays dormant until a supported tokenizer
/// ships; <see cref="UsesFallback"/> reports which path is active.
/// </remarks>
public sealed class OnnxEmbeddingProvider : IEmbeddingProvider, IDisposable
{
    /// <summary>Default ONNX embedding model path, relative to the working directory.</summary>
    public const string DefaultModelPath = "models/embedding_model.onnx";

    /// <summary>Default HuggingFace tokenizer path, relative to the working directory.</summary>
    public const string DefaultTokenizerPath = "models/embedding_tokenizer.json";

    private readonly ILogger<OnnxEmbeddingProvider> _log;
    private readonly FakeEmbeddingProvider _fallback = new();
    private bool _disposed;

    /// <summary>
    /// Creates the provider; falls back to fake embeddings when files are missing
    /// or the HF tokenizer format is unsupported.
    /// </summary>
    /// <param name="logger">Logger (defaults to null logger).</param>
    /// <param name="modelPath">Path to the embedding ONNX model.</param>
    /// <param name="tokenizerPath">Path to the HuggingFace tokenizer.json.</param>
    public OnnxEmbeddingProvider(
        ILogger<OnnxEmbeddingProvider>? logger = null,
        string modelPath = DefaultModelPath,
        string tokenizerPath = DefaultTokenizerPath)
    {
        _log = logger ?? NullLogger<OnnxEmbeddingProvider>.Instance;
        if (!File.Exists(modelPath) || !File.Exists(tokenizerPath))
        {
            _log.LogWarning(
                "Embedding model files not found (model: {Model}, tokenizer: {Tokenizer}); using fake embeddings.",
                modelPath,
                tokenizerPath);
            UsesFallback = true;
            Dimensions = _fallback.Dimensions;
            return;
        }

        // No Microsoft.ML.Tokenizers 2.0.0 API loads a generic HF tokenizer.json
        // (see class remarks). Log once and use the deterministic fallback.
        _log.LogWarning("HF tokenizer not supported by Microsoft.ML.Tokenizers 2.0.0; using fake embeddings.");
        UsesFallback = true;
        Dimensions = _fallback.Dimensions;
    }

    /// <summary>True when serving deterministic fake embeddings instead of a real model.</summary>
    public bool UsesFallback { get; }

    /// <inheritdoc />
    public int Dimensions { get; }

    /// <inheritdoc />
    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _fallback.EmbedAsync(text, ct);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
    }
}
