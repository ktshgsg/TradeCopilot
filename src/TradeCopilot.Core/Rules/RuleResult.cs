namespace TradeCopilot.Core.Rules;

/// <summary>Outcome of a rule-engine evaluation.</summary>
/// <param name="Matched">True when a rule produced the answer.</param>
/// <param name="AnswerText">Answer to show the user; empty when nothing matched.</param>
/// <param name="RuleName">Name of the matching rule; empty when nothing matched.</param>
public sealed record RuleResult(bool Matched, string AnswerText, string RuleName)
{
    /// <summary>No rule applied.</summary>
    public static RuleResult NoMatch { get; } = new(false, string.Empty, string.Empty);
}
