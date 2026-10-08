using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Embeddings;

namespace TradeCopilot.Core.Cache;

/// <summary>
/// SQLite-backed semantic cache. Uses the <c>sqlite-vec</c> vec0 extension when
/// <c>vec0.dll</c> sits next to the executable; otherwise falls back to an
/// in-memory FIFO (500 entries) cosine-similarity scan. Serialized via SemaphoreSlim.
/// </summary>
/// <remarks>
/// The vec0 column is declared with <c>distance_metric = cosine</c>, so the KNN
/// <c>distance</c> column is cosine distance and <c>similarity = 1 - distance</c>
/// (per the sqlite-vec docs; the vec0 default would otherwise be L2). The fallback
/// path computes cosine similarity directly. Vectors are bound as packed float32
/// little-endian BLOBs.
/// </remarks>
public sealed partial class SqliteSemanticCache : ISemanticCache
{
    /// <summary>Minimum cosine similarity for a cache hit.</summary>
    public const float SimilarityThreshold = 0.88f;

    private const int FallbackCapacity = 500;
    private const int QueryTopK = 10;

    private readonly IEmbeddingProvider _embeddings;
    private readonly ILogger<SqliteSemanticCache> _log;
    private readonly bool _encryptCache;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<(long Id, float[] Vector, string ContextHash)> _fallbackVectors = [];

    private SqliteConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Creates the cache over the given embedding provider.
    /// </summary>
    /// <param name="embeddings">Embedding provider.</param>
    /// <param name="logger">Logger (defaults to null logger).</param>
    /// <param name="encryptCache">When true, answers are DPAPI-encrypted at rest.</param>
    public SqliteSemanticCache(IEmbeddingProvider embeddings, ILogger<SqliteSemanticCache>? logger = null, bool encryptCache = false)
    {
        _embeddings = embeddings ?? throw new ArgumentNullException(nameof(embeddings));
        _log = logger ?? NullLogger<SqliteSemanticCache>.Instance;
        _encryptCache = encryptCache;
    }

    /// <summary>True when the vec0 extension loaded and vector SQL is active.</summary>
    public bool UseVectorExtension { get; private set; }
}
