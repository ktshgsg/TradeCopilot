using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace TradeCopilot.Core.Cache;

public sealed partial class SqliteSemanticCache
{
    /// <inheritdoc />
    public async Task InitializeAsync(string dbPath, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dbPath);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _connection?.Dispose();
            _connection = new SqliteConnection($"Data Source={dbPath}");
            await _connection.OpenAsync(ct).ConfigureAwait(false);
            _connection.EnableExtensions(true);
            try
            {
                _connection.LoadExtension("vec0");
                UseVectorExtension = true;
            }
            catch (Exception ex)
            {
                UseVectorExtension = false;
                _log.LogWarning(ex, "sqlite-vec vec0 extension not found; falling back to in-memory cosine similarity.");
            }

            string[] ddl =
            [
                """
                CREATE TABLE IF NOT EXISTS cache_entries (
                  id INTEGER PRIMARY KEY AUTOINCREMENT,
                  query_text TEXT NOT NULL,
                  answer_text TEXT NOT NULL,
                  language_code TEXT NOT NULL,
                  chart_context_hash TEXT NOT NULL,
                  created_at INTEGER NOT NULL,
                  hit_count INTEGER DEFAULT 0
                );
                """,
                "CREATE INDEX IF NOT EXISTS idx_context ON cache_entries(chart_context_hash);",
                "CREATE INDEX IF NOT EXISTS idx_created ON cache_entries(created_at);",
            ];
            foreach (string sql in ddl)
            {
                using var cmd = new SqliteCommand(sql, _connection);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            if (UseVectorExtension)
            {
                try
                {
                    string vecDdl = string.Create(
                        System.Globalization.CultureInfo.InvariantCulture,
                        $"CREATE VIRTUAL TABLE IF NOT EXISTS query_vectors USING vec0(embedding float[{_embeddings.Dimensions}] distance_metric = cosine);");
                    using var vecCmd = new SqliteCommand(vecDdl, _connection);
                    await vecCmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    UseVectorExtension = false;
                    _log.LogWarning(ex, "Failed to create the vec0 table; falling back to in-memory cosine similarity.");
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
