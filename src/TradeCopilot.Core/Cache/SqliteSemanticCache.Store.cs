using Microsoft.Data.Sqlite;

namespace TradeCopilot.Core.Cache;

public sealed partial class SqliteSemanticCache
{
    /// <inheritdoc />
    public async Task StoreAsync(
        string queryText, string answerText, string languageCode, string chartContextHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(queryText);
        ArgumentNullException.ThrowIfNull(answerText);
        ArgumentNullException.ThrowIfNull(languageCode);
        ArgumentNullException.ThrowIfNull(chartContextHash);
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfUninitialized();

        float[] vector = await _embeddings.EmbedAsync(queryText, ct).ConfigureAwait(false);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            using SqliteTransaction tx = (SqliteTransaction)await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
            await using (tx.ConfigureAwait(false))
            {
                long id;
                using (var insert = new SqliteCommand(
                    "INSERT INTO cache_entries (query_text, answer_text, language_code, chart_context_hash, created_at, hit_count) " +
                    "VALUES (@p0, @p1, @p2, @p3, strftime('%s','now'), 0);",
                    _connection,
                    tx))
                {
                    insert.Parameters.AddWithValue("@p0", queryText);
                    insert.Parameters.AddWithValue("@p1", answerText);
                    insert.Parameters.AddWithValue("@p2", languageCode);
                    insert.Parameters.AddWithValue("@p3", chartContextHash);
                    await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                using (var lastId = new SqliteCommand("SELECT last_insert_rowid();", _connection, tx))
                {
                    object? scalar = await lastId.ExecuteScalarAsync(ct).ConfigureAwait(false);
                    id = Convert.ToInt64(scalar, System.Globalization.CultureInfo.InvariantCulture);
                }

                if (UseVectorExtension)
                {
                    using var vecInsert = new SqliteCommand(
                        "INSERT INTO query_vectors(rowid, embedding) VALUES (@p0, @p1);",
                        _connection,
                        tx);
                    vecInsert.Parameters.AddWithValue("@p0", id);
                    vecInsert.Parameters.AddWithValue("@p1", PackFloats(vector));
                    await vecInsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                else
                {
                    _fallbackVectors.Add((id, vector, chartContextHash));
                    while (_fallbackVectors.Count > FallbackCapacity)
                    {
                        _fallbackVectors.RemoveAt(0);
                    }
                }

                await tx.CommitAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
