using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules;

/// <summary>
/// A single deterministic trading rule. Implementations must be stateless and thread-safe.
/// </summary>
internal interface IRule
{
    /// <summary>Stable snake_case rule identifier, e.g. <c>rsi_overbought</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Evaluates the rule. Returns <see langword="null"/> when the rule does not apply;
    /// a <see cref="RuleResult"/> means the rule matched. Never throws for null or
    /// empty inputs.
    /// </summary>
    /// <param name="query">User question (may be empty).</param>
    /// <param name="indicators">Parsed chart indicators.</param>
    /// <param name="patterns">Detected chart patterns (may be empty).</param>
    /// <param name="language">Detected query language.</param>
    RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language);
}
