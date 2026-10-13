using FluentAssertions;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Embeddings;

namespace TradeCopilot.Tests;

/// <summary>
/// Semantic cache tests: store/query, similarity thresholding, hit counting,
/// and chart-context preference.
/// </summary>
public sealed class SemanticCacheTests : IDisposable
{
    private readonly string _dir;

    /// <summary>Creates a per-test temp directory for cache databases.</summary>
    public SemanticCacheTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"tc-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>Exact re-query hits with similarity 1, increments hit_count, matches context.</summary>
    [Fact]
    public async Task Store_ThenQueryExactText_HitsAndCountsHit()
    {
        await using SqliteSemanticCache cache = await NewCacheAsync(new FakeEmbeddingProvider());
        await cache.StoreAsync("how to trade a bull flag", "Buy the breakout.", "en-US", "ctx-a");

        CacheLookupResult hit = await cache.QueryAsync("how to trade a bull flag", "ctx-a");

        hit.Hit.Should().BeTrue();
        hit.AnswerText.Should().Be("Buy the breakout.");
        hit.Similarity.Should().BeApproximately(1f, 1e-4f);
        hit.ContextMatches.Should().BeTrue();
        hit.EntryId.Should().NotBeNull();

        IReadOnlyList<CacheEntry> recent = await cache.GetRecentAsync(10);
        recent.Should().ContainSingle();
        recent[0].HitCount.Should().Be(1);
        recent[0].LanguageCode.Should().Be("en-US");
        (await cache.GetRecentAsync(0)).Should().BeEmpty();
    }

    /// <summary>An unrelated question falls below the 0.88 similarity threshold.</summary>
    [Fact]
    public async Task Query_UnrelatedText_Misses()
    {
        await using SqliteSemanticCache cache = await NewCacheAsync(new FakeEmbeddingProvider());
        await cache.StoreAsync("how to trade a bull flag", "Buy the breakout.", "en-US", "ctx-a");

        CacheLookupResult miss = await cache.QueryAsync("latest inflation report in argentina", "ctx-a");

        miss.Hit.Should().BeFalse();
        miss.AnswerText.Should().BeNull();
        miss.EntryId.Should().BeNull();
        miss.Similarity.Should().BeLessThan(SqliteSemanticCache.SimilarityThreshold);
    }

    /// <summary>A near variant above the threshold hits and reports a context mismatch.</summary>
    [Fact]
    public async Task Query_SimilarText_HitsAboveThreshold_WithContextMismatch()
    {
        await using SqliteSemanticCache cache = await NewCacheAsync(new StubEmbeddingProvider());
        await cache.StoreAsync("bitcoin", "BTC analysis.", "en-US", "ctx-1");

        CacheLookupResult hit = await cache.QueryAsync("btc", "ctx-2");

        hit.Hit.Should().BeTrue();
        hit.AnswerText.Should().Be("BTC analysis.");
        hit.Similarity.Should().BeApproximately(0.96f, 1e-4f);
        hit.ContextMatches.Should().BeFalse();
    }

    /// <summary>Within the similarity band, the chart-context match is preferred.</summary>
    [Fact]
    public async Task Query_PrefersChartContextMatch_WithinSimilarityBand()
    {
        await using SqliteSemanticCache cache = await NewCacheAsync(new StubEmbeddingProvider());
        await cache.StoreAsync("close-b", "answer B", "en-US", "ctx-b");
        await cache.StoreAsync("close-a", "answer A", "en-US", "ctx-a");

        CacheLookupResult hit = await cache.QueryAsync("query", "ctx-a");

        hit.Hit.Should().BeTrue();
        hit.AnswerText.Should().Be("answer A");
        hit.ContextMatches.Should().BeTrue();

        CacheLookupResult noContext = await cache.QueryAsync("query", "ctx-c");
        noContext.Hit.Should().BeTrue();
        noContext.AnswerText.Should().Be("answer B");
        noContext.ContextMatches.Should().BeFalse();
    }

    private async Task<SqliteSemanticCache> NewCacheAsync(IEmbeddingProvider embeddings)
    {
        var cache = new SqliteSemanticCache(embeddings, encryptCache: false);
        await cache.InitializeAsync(Path.Combine(_dir, $"{Guid.NewGuid():N}.db"));
        return cache;
    }
}
