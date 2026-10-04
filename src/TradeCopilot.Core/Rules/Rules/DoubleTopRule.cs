using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when a double-top pattern is detected with confidence above 0.6.</summary>
internal sealed class DoubleTopRule : IRule
{
    /// <inheritdoc />
    public string Name => "double_top";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (!RuleMatch.HasPattern(patterns, PatternLabels.DoubleTop, 0.6f))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            "Double top detected. Suggests a bearish reversal near resistance; consider tightening stops.",
            "Double top pattern mila hai. Resistance ke paas bearish reversal suggest karta hai; stop loss tight karne ka socho.");
        return new RuleResult(true, answer, Name);
    }
}
