using System.Diagnostics;
using Microsoft.Extensions.Logging;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Capture;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Rules;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Orchestrator;

/// <summary>
/// Answers user queries by trying three stages in order — semantic cache, deterministic
/// rules, then the local LLM — and returning the first usable answer.
/// </summary>
/// <remarks>
/// Every stage is independently fault-tolerant: a failing cache falls through to the
/// rules, failing rules fall through to the LLM, and a failing or unavailable LLM returns
/// a fixed fallback string. <see cref="AnswerAsync"/> therefore never throws and always
/// returns a <see cref="QueryResult"/>.
/// </remarks>
public sealed class AgentOrchestrator
{
    /// <summary>Similarity at or above which a cache hit is trusted.</summary>
    public const float CacheSimilarityThreshold = 0.9f;

    /// <summary>Language code for <see cref="Language.En"/>.</summary>
    public const string LanguageEn = "en";

    /// <summary>Language code for <see cref="Language.Hi"/> (Devanagari).</summary>
    public const string LanguageHi = "hi";

    /// <summary>Language code for <see cref="Language.Hinglish"/>.</summary>
    public const string LanguageHinglish = "hinglish";

    /// <summary>Returned when the LLM is offline or fails.</summary>
    public const string LlmFallbackAnswer = "Sorry, main abhi jawab nahi de sakta. (LLM offline)";

    /// <summary>Source marker for an empty query.</summary>
    public const string SourceEmpty = "empty";

    private const string SourceCache = "cache";
    private const string SourceRule = "rule";
    private const string SourceVision = "vision";
    private const string SourceLlm = "llm";

    private readonly AgentState _state;
    private readonly ISemanticCache _cache;
    private readonly IRuleEngine _rules;
    private readonly ILlmEngine _llm;
    private readonly IVisionEngine _vision;
    private readonly ILogger<AgentOrchestrator> _log;

    /// <summary>Creates the orchestrator over the given stages.</summary>
    /// <param name="state">Current chart state.</param>
    /// <param name="cache">Semantic answer cache.</param>
    /// <param name="rules">Deterministic rule engine.</param>
    /// <param name="llm">Local LLM engine.</param>
    /// <param name="vision">Vision engine for visual questions.</param>
    /// <param name="log">Logger.</param>
    public AgentOrchestrator(
        AgentState state,
        ISemanticCache cache,
        IRuleEngine rules,
        ILlmEngine llm,
        IVisionEngine vision,
        ILogger<AgentOrchestrator> log)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(llm);
        ArgumentNullException.ThrowIfNull(vision);
        ArgumentNullException.ThrowIfNull(log);
        _state = state;
        _cache = cache;
        _rules = rules;
        _llm = llm;
        _vision = vision;
        _log = log;
    }
    /// <summary>
    /// Answers <paramref name="query"/> using cache, then rules, then the LLM.
    /// Never throws: each stage failure is logged and falls through to the next.
    /// </summary>
    /// <param name="query">User question.</param>
    /// <param name="ct">Cancellation token; only honoured while a stage is awaiting.</param>
    /// <returns>The answer plus which stage produced it.</returns>
    public async Task<QueryResult> AnswerAsync(string query, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        if (string.IsNullOrWhiteSpace(query))
        {
            return new QueryResult(string.Empty, LanguageEn, SourceEmpty, stopwatch.ElapsedMilliseconds);
        }

        AgentSnapshot snapshot = _state.Snapshot();
        string langCode = MapLanguage(LanguageDetector.Detect(query));

        CacheLookupResult? cacheResult = null;
        try
        {
            cacheResult = await _cache.QueryAsync(query, snapshot.ContextHash, ct).ConfigureAwait(false);
            if (IsUsableCacheHit(cacheResult))
            {
                QueryResult cached = new(
                    cacheResult.AnswerText!,
                    langCode,
                    SourceCache,
                    stopwatch.ElapsedMilliseconds);
                LogAnswered(cached, cacheResult.Similarity);
                return cached;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "Semantic cache lookup failed; continuing to the rule engine.");
        }

        try
        {
            RuleResult ruleResult = _rules.Evaluate(query, snapshot.Ocr ?? OcrResult.Empty, snapshot.Patterns);
            if (ruleResult.Matched)
            {
                await TryStoreAsync(query, ruleResult.AnswerText, langCode, snapshot.ContextHash, ct)
                    .ConfigureAwait(false);
                QueryResult ruled = new(
                    ruleResult.AnswerText,
                    langCode,
                    SourceRule,
                    stopwatch.ElapsedMilliseconds);
                LogAnswered(ruled, null);
                return ruled;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "Rule evaluation failed; continuing to the LLM.");
        }

        // Vision stage: only for visual questions when a frame is available and the model
        // loaded. Any failure or empty answer falls through to the LLM exactly as before.
        FrameData? frame = snapshot.Frame;
        if (_vision.Enabled && frame is not null && QueryIsVisual(query))
        {
            try
            {
                string answer = await _vision.DescribeAsync(frame, query, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    await TryStoreAsync(query, answer, langCode, snapshot.ContextHash, ct).ConfigureAwait(false);

                    QueryResult visionResult = new(answer, langCode, SourceVision, stopwatch.ElapsedMilliseconds);
                    LogAnswered(visionResult, null);
                    return visionResult;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _log.LogError(ex, "Vision description failed; continuing to the LLM.");
            }
        }

        try
        {
            string system = PromptBuilder.BuildSystemPrompt();
            string user = PromptBuilder.BuildUserPrompt(
                query,
                snapshot.Ocr,
                snapshot.Patterns,
                cacheResult?.AnswerText);
            string answer = await _llm.CompleteAsync(system, user, ct).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(answer) || answer == LlamaSharpEngine.NotAvailableMessage)
            {
                answer = LlmFallbackAnswer;
            }
            else
            {
                await TryStoreAsync(query, answer, langCode, snapshot.ContextHash, ct).ConfigureAwait(false);
            }

            QueryResult llmResult = new(answer, langCode, SourceLlm, stopwatch.ElapsedMilliseconds);
            LogAnswered(llmResult, null);
            return llmResult;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogError(ex, "LLM completion failed; returning the offline fallback.");
            return new QueryResult(LlmFallbackAnswer, langCode, SourceLlm, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task TryStoreAsync(
        string query,
        string answer,
        string langCode,
        string contextHash,
        CancellationToken ct)
    {
        try
        {
            await _cache.StoreAsync(query, answer, langCode, contextHash, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The answer is already computed; a cache write failure must not fail the query.
            _log.LogWarning(ex, "Failed to store answer in the semantic cache.");
        }
    }

    /// <summary>
    /// Substrings (matched case-insensitively) that mark a question as visual so it is
    /// offered to the vision stage before the LLM.
    /// </summary>
    private static readonly string[] VisionKeywords =
    [
        "chart", "graph", "pattern", "trend", "candle", "candlestick", "breakout",
        "support", "resistance", "bullish", "bearish", "volume", "trendline", "drawn",
        "shape", "screenshot", "image", "picture", "visual", "visible", "indicator",
        "oscillator", "describe", "look",
    ];

    /// <summary>True when the query is asking what is visible on the screen.</summary>
    private static bool QueryIsVisual(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        foreach (string keyword in VisionKeywords)
        {
            if (query.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsUsableCacheHit(CacheLookupResult result) =>
        result.Hit
        && result.ContextMatches
        && result.Similarity >= CacheSimilarityThreshold
        && !string.IsNullOrEmpty(result.AnswerText);

    private void LogAnswered(QueryResult result, float? similarity)
    {
        if (similarity is null)
        {
            _log.LogInformation("Query answered {Source} in {ElapsedMs}ms", result.Source, result.ElapsedMs);
        }
        else
        {
            _log.LogInformation(
                "Query answered {Source} in {ElapsedMs}ms (sim={Sim})",
                result.Source,
                result.ElapsedMs,
                similarity.Value);
        }
    }

    /// <summary>Maps a detected <see cref="Language"/> to its wire code.</summary>
    internal static string MapLanguage(Language language) => language switch
    {
        Language.Hi => LanguageHi,
        Language.Hinglish => LanguageHinglish,
        _ => LanguageEn,
    };
}
