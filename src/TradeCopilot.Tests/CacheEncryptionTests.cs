using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Embeddings;
using TradeCopilot.Core.Privacy;

namespace TradeCopilot.Tests;

/// <summary>Tests for DPAPI answer encryption at rest.</summary>
public sealed class CacheEncryptionTests : IDisposable
{
    private const string Answer = "RSI overbought hai.";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tc-enc-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates the temp directory for test databases.</summary>
    public CacheEncryptionTests()
    {
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Removes the temp directory.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Encrypt then Decrypt round-trips a Hinglish answer exactly.</summary>
    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        string cipher = CacheEncryption.Encrypt(Answer);

        CacheEncryption.Decrypt(cipher).Should().Be(Answer);
    }

    /// <summary>Empty input passes through Encrypt unchanged.</summary>
    [Fact]
    public void Encrypt_Empty_ReturnsEmpty()
    {
        CacheEncryption.Encrypt(string.Empty).Should().Be(string.Empty);
    }

    /// <summary>Empty input passes through Decrypt unchanged.</summary>
    [Fact]
    public void Decrypt_Empty_ReturnsEmpty()
    {
        CacheEncryption.Decrypt(string.Empty).Should().Be(string.Empty);
    }

    /// <summary>With encryption on, the raw answer_text column must be opaque.</summary>
    [Fact]
    public async Task Store_WithEncryption_RawColumnIsOpaque()
    {
        string db = Path.Combine(_dir, "enc.db");
        await using var cache = new SqliteSemanticCache(new FakeEmbeddingProvider(), NullLogger<SqliteSemanticCache>.Instance, encryptCache: true);
        await cache.InitializeAsync(db);
        await cache.StoreAsync("RSI kya hai?", Answer, "hinglish", "ctx");

        string raw = ReadRawAnswer(db);

        raw.Should().NotContain("RSI");
        raw.Should().NotBe(Answer);
    }

    /// <summary>With encryption on, QueryAsync still returns the readable answer.</summary>
    [Fact]
    public async Task Query_WithEncryption_ReturnsReadableAnswer()
    {
        string db = Path.Combine(_dir, "enc-query.db");
        await using var cache = new SqliteSemanticCache(new FakeEmbeddingProvider(), NullLogger<SqliteSemanticCache>.Instance, encryptCache: true);
        await cache.InitializeAsync(db);
        await cache.StoreAsync("RSI kya hai?", Answer, "hinglish", "ctx");

        CacheLookupResult hit = await cache.QueryAsync("RSI kya hai?", "ctx");

        hit.Hit.Should().BeTrue();
        hit.AnswerText.Should().Be(Answer);
    }

    /// <summary>With encryption off, the raw column stays plaintext.</summary>
    [Fact]
    public async Task Store_WithoutEncryption_RawColumnIsPlaintext()
    {
        string db = Path.Combine(_dir, "plain.db");
        await using var cache = new SqliteSemanticCache(new FakeEmbeddingProvider(), NullLogger<SqliteSemanticCache>.Instance, encryptCache: false);
        await cache.InitializeAsync(db);
        await cache.StoreAsync("RSI kya hai?", Answer, "hinglish", "ctx");

        ReadRawAnswer(db).Should().Be(Answer);
    }

    private static string ReadRawAnswer(string db)
    {
        using var connection = new SqliteConnection($"Data Source={db}");
        connection.Open();
        using var cmd = new SqliteCommand("SELECT answer_text FROM cache_entries LIMIT 1;", connection);
        return (string)cmd.ExecuteScalar()!;
    }
}
