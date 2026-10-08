using System.Data.Common;
using Microsoft.Data.Sqlite;
using TradeCopilot.Core.Privacy;

namespace TradeCopilot.Core.Cache;

public sealed partial class SqliteSemanticCache
{
    /// <summary>
    /// Similarity band within which a chart-context match wins over a marginally
    /// higher vector similarity.
    /// </summary>
    private const float ContextPreferenceBand = 0.01f;

    /// <inheritdoc />
    public async Task<CacheLookupResult> QueryAsync(
        string queryText, string chartContextHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(chartContextHash);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfUninitialized();

        float[] query = await _embeddings.EmbedAsync(queryText, ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var candidates = new List<(long Id, float Similarity, string ContextHash)>();
            if (UseVectorExtension)
            {
                using var cmd = new SqliteCommand(
                    "SELECT v.rowid, v.distance, e.chart_context_hash " +
                    "FROM query_vectors v JOIN cache_entries e ON e.id = v.rowid " +
                    "WHERE v.embedding MATCH @p0 AND k = @p1 ORDER BY v.distance;", _connection);
                cmd.Parameters.AddWithValue("@p0", PackFloats(query));
                cmd.Parameters.AddWithValue("@p1", QueryTopK);
                using DbDataReader reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await reader.ReadAsync(ct).ConfigureAwait(false))
                {
                    // The vec0 column is declared with distance_metric = cosine.
                    candidates.Add((reader.GetInt64(0), 1f - (float)reader.GetDouble(1), reader.GetString(2)));
                }
            }
            else
            {
                foreach ((long id, float[] vector, string contextHash) in _fallbackVectors)
                {
                    candidates.Add((id, CosineSimilarity(query, vector), contextHash));
                }
            }

            (long Id, float Similarity, bool ContextMatches)? best = SelectBest(candidates, chartContextHash);
            if (best is not { } hit || hit.Similarity < SimilarityThreshold)
            {
                return new CacheLookupResult(false, null, best?.Similarity ?? 0f, false, null);
            }

            string? answer = await FetchAnswerAsync(hit.Id, ct).ConfigureAwait(false);
            if (answer is null)
            {
                return new CacheLookupResult(false, null, hit.Similarity, false, null);
            }

            if (_encryptCache)
            {
                answer = CacheEncryption.Decrypt(answer);
            }

            using var bump = new SqliteCommand(
                "UPDATE cache_entries SET hit_count = hit_count + 1 WHERE id = @p0;", _connection);
            bump.Parameters.AddWithValue("@p0", hit.Id);
            await bump.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new CacheLookupResult(true, answer, hit.Similarity, hit.ContextMatches, hit.Id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Picks the best candidate by cosine similarity, preferring a matching
    /// chart context among candidates within <see cref="ContextPreferenceBand"/>.
    /// </summary>
    private static (long Id, float Similarity, bool ContextMatches)? SelectBest(
        List<(long Id, float Similarity, string ContextHash)> candidates, string chartContextHash)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        candidates.Sort(static (a, b) => b.Similarity.CompareTo(a.Similarity));
        float bestSimilarity = candidates[0].Similarity;
        foreach ((long id, float similarity, string contextHash) in candidates)
        {
            if (similarity < bestSimilarity - ContextPreferenceBand)
            {
                break;
            }

            if (string.Equals(contextHash, chartContextHash, StringComparison.Ordinal))
            {
                return (id, similarity, true);
            }
        }

        (long topId, float topSimilarity, string topContext) = candidates[0];
        return (topId, topSimilarity, string.Equals(topContext, chartContextHash, StringComparison.Ordinal));
    }

    /// <summary>Cosine similarity of two unit-length vectors, clamped to [-1, 1].</summary>
    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length)
        {
            return 0f;
        }

        float dot = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
        }

        return Math.Clamp(dot, -1f, 1f);
    }

    private async Task<string?> FetchAnswerAsync(long id, CancellationToken ct)
    {
        using var cmd = new SqliteCommand("SELECT answer_text FROM cache_entries WHERE id = @p0;", _connection);
        cmd.Parameters.AddWithValue("@p0", id);
        object? value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return value as string;
    }
}
