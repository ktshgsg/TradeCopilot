using FluentAssertions;
using Microsoft.Data.Sqlite;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Embeddings;

namespace TradeCopilot.Tests;

/// <summary>
/// Cache maintenance tests: age-based purging, recent listing, and
/// persistence across cache instances.
/// </summary>
public sealed class SemanticCacheMaintenanceTests : IDisposable
{
    private readonly string _dir;
    private string _dbPath = string.Empty;

    /// <summary>Creates a per-test temp directory for cache databases.</summary>
    public SemanticCacheMaintenanceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"tc-cache-maint-{Guid.NewGuid():N}");
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

    /// <summary>Purge removes aged entries with fewer than 3 hits and keeps popular ones.</summary>
    [Fact]
    public async Task Purge_RemovesAgedUnpopularEntries_KeepsHitEntries()
    {
        await using SqliteSemanticCache cache = await NewCacheAsync();
        await cache.StoreAsync("popular question", "popular answer", "en-US", "ctx");
        await cache.StoreAsync("stale question", "stale answer", "en-US", "ctx");
        for (int i = 0; i < 3; i++)
        {
            CacheLookupResult hit = await cache.QueryAsync("popular question", "ctx");
            hit.Hit.Should().BeTrue();
        }

        AgeAllEntries(days: 30);
        await cache.PurgeOlderThanAsync(TimeSpan.FromDays(7));

        IReadOnlyList<CacheEntry> remaining = await cache.GetRecentAsync(10);
        remaining.Should().ContainSingle();
        remaining[0].QueryText.Should().Be("popular question");
        remaining[0].HitCount.Should().Be(3);
    }

    /// <summary>GetRecentAsync returns the newest entries first, capped by count.</summary>
    [Fact]
    public async Task GetRecent_ReturnsNewestFirst_AndHonorsLimit()
    {
        await using SqliteSemanticCache cache = await NewCacheAsync();
        await cache.StoreAsync("first", "1", "en-US", "ctx");
        await cache.StoreAsync("second", "2", "en-US", "ctx");
        await cache.StoreAsync("third", "3", "en-US", "ctx");

        IReadOnlyList<CacheEntry> recent = await cache.GetRecentAsync(2);

        recent.Should().HaveCount(2);
        recent[0].QueryText.Should().Be("third");
        recent[1].QueryText.Should().Be("second");
    }

    /// <summary>Entries persist across cache instances over the same database file.</summary>
    [Fact]
    public async Task ReinitializingOverSameDatabase_FindsStoredEntries()
    {
        string path = Path.Combine(_dir, "persist.db");
        await using (var first = new SqliteSemanticCache(new FakeEmbeddingProvider(), encryptCache: false))
        {
            await first.InitializeAsync(path);
            await first.StoreAsync("q", "a", "en-US", "ctx");
        }

        await using var second = new SqliteSemanticCache(new FakeEmbeddingProvider(), encryptCache: false);
        await second.InitializeAsync(path);

        IReadOnlyList<CacheEntry> entries = await second.GetRecentAsync(5);
        entries.Should().ContainSingle();
        entries[0].QueryText.Should().Be("q");
    }

    private async Task<SqliteSemanticCache> NewCacheAsync()
    {
        _dbPath = Path.Combine(_dir, $"{Guid.NewGuid():N}.db");
        var cache = new SqliteSemanticCache(new FakeEmbeddingProvider(), encryptCache: false);
        await cache.InitializeAsync(_dbPath);
        return cache;
    }

    private void AgeAllEntries(int days)
    {
        using var db = new SqliteConnection($"Data Source={_dbPath}");
        db.Open();
        using var cmd = new SqliteCommand($"UPDATE cache_entries SET created_at = created_at - {days * 86400};", db);
        cmd.ExecuteNonQuery();
    }
}
