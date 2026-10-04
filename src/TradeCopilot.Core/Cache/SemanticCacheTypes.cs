namespace TradeCopilot.Core.Cache;

/// <summary>
/// A stored question/answer pair with its vector-search metadata.
/// </summary>
/// <param name="Id">Row id.</param>
/// <param name="QueryText">Original user question.</param>
/// <param name="AnswerText">Stored answer.</param>
/// <param name="LanguageCode">BCP-47 answer language.</param>
/// <param name="ChartContextHash">Chart fingerprint at store time.</param>
/// <param name="CreatedAtUnix">Creation time, Unix seconds.</param>
/// <param name="HitCount">Number of cache hits served.</param>
public sealed record CacheEntry(
    long Id,
    string QueryText,
    string AnswerText,
    string LanguageCode,
    string ChartContextHash,
    long CreatedAtUnix,
    int HitCount);

/// <summary>
/// Result of a semantic cache lookup.
/// </summary>
/// <param name="Hit">True when a sufficiently similar entry was found.</param>
/// <param name="AnswerText">Stored answer, or null on miss.</param>
/// <param name="Similarity">Cosine similarity of the best candidate in [0, 1].</param>
/// <param name="ContextMatches">True when the best hit shares the chart context hash.</param>
/// <param name="EntryId">Row id of the hit, or null on miss.</param>
public sealed record CacheLookupResult(
    bool Hit,
    string? AnswerText,
    float Similarity,
    bool ContextMatches,
    long? EntryId);

/// <summary>
/// Semantic answer cache: vector similarity plus chart-context matching.
/// </summary>
public interface ISemanticCache : IAsyncDisposable
{
    /// <summary>
    /// Opens the database and creates tables (and the vec0 table when available).
    /// </summary>
    /// <param name="dbPath">SQLite file path.</param>
    /// <param name="ct">Cancellation token.</param>
    Task InitializeAsync(string dbPath, CancellationToken ct = default);

    /// <summary>
    /// Finds the most similar stored answer at or above the similarity threshold.
    /// </summary>
    /// <param name="queryText">Incoming question.</param>
    /// <param name="chartContextHash">Current chart fingerprint.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<CacheLookupResult> QueryAsync(string queryText, string chartContextHash, CancellationToken ct = default);

    /// <summary>
    /// Embeds and stores a question/answer pair.
    /// </summary>
    /// <param name="queryText">User question.</param>
    /// <param name="answerText">Answer to cache.</param>
    /// <param name="languageCode">BCP-47 answer language.</param>
    /// <param name="chartContextHash">Chart fingerprint at store time.</param>
    /// <param name="ct">Cancellation token.</param>
    Task StoreAsync(string queryText, string answerText, string languageCode, string chartContextHash, CancellationToken ct = default);

    /// <summary>
    /// Deletes entries older than <paramref name="age"/> with fewer than 3 hits.
    /// </summary>
    /// <param name="age">Maximum entry age to keep.</param>
    /// <param name="ct">Cancellation token.</param>
    Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default);

    /// <summary>
    /// Returns the most recent entries first.
    /// </summary>
    /// <param name="count">Maximum entries to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<CacheEntry>> GetRecentAsync(int count, CancellationToken ct = default);
}
