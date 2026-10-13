namespace TradeCopilot.Core.Orchestrator;

/// <summary>
/// The outcome of answering one user query.
/// </summary>
/// <param name="AnswerText">The answer shown to the user; never null.</param>
/// <param name="LanguageCode">Answer language: "en", "hi", or "hinglish".</param>
/// <param name="Source">Which stage produced the answer: "cache", "rule", "vision", "llm", or "empty".</param>
/// <param name="ElapsedMs">Wall-clock time spent answering, in milliseconds.</param>
public sealed record QueryResult(
    string AnswerText,
    string LanguageCode,
    string Source,
    long ElapsedMs);
