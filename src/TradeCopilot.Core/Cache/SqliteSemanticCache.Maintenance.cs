using System.Data.Common;
using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace TradeCopilot.Core.Cache;

public sealed partial class SqliteSemanticCache
{
    /// <inheritdoc />
    public async Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfUninitialized();
        long cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (long)age.TotalSeconds;
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var ids = new List<long>();
            using (var select = new SqliteCommand(
                "SELECT id FROM cache_entries WHERE created_at < @p0 AND hit_count < 3;", _connection))
            {
                select.Parameters.AddWithValue("@p0", cutoff);
                using DbDataReader reader = await select.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    ids.Add(reader.GetInt64(0));
                }
            }

            foreach (long id in ids)
            {
                using var delete = new SqliteCommand("DELETE FROM cache_entries WHERE id = @p0;", _connection);
                delete.Parameters.AddWithValue("@p0", id);
                await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

                if (UseVectorExtension)
                {
                    using var deleteVector = new SqliteCommand("DELETE FROM query_vectors WHERE rowid = @p0;", _connection);
                    deleteVector.Parameters.AddWithValue("@p0", id);
                    await deleteVector.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
            }

            if (ids.Count > 0)
            {
                var purged = new HashSet<long>(ids);
                _fallbackVectors.RemoveAll(v => purged.Contains(v.Id));
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CacheEntry>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfUninitialized();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return [];
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using var cmd = new SqliteCommand(
                "SELECT id, query_text, answer_text, language_code, chart_context_hash, created_at, hit_count " +
                "FROM cache_entries ORDER BY created_at DESC, id DESC LIMIT @p0;", _connection);
            cmd.Parameters.AddWithValue("@p0", count);
            var entries = new List<CacheEntry>();
            using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                entries.Add(new CacheEntry(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetInt64(5),
                    (int)reader.GetInt64(6)));
            }

            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _connection?.Dispose();
            _connection = null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Packs a float vector as little-endian float32 bytes for a vec0 BLOB.</summary>
    private static byte[] PackFloats(float[] vector) => MemoryMarshal.AsBytes(vector.AsSpan()).ToArray();

    private void ThrowIfUninitialized()
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("Cache is not initialized; call InitializeAsync first.");
        }
    }
}
