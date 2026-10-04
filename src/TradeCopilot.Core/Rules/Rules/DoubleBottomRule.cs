using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when a double-bottom pattern is detected with confidence above 0.6.</summary>
internal sealed class DoubleBottomRule : IRule
{
    /// <inheritdoc />
    public string Name => "double_bottom";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (!RuleMatch.HasPattern(patterns, PatternLabels.DoubleBottom, 0.6f))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            "Double bottom detected. Suggests a bullish reversal near support; watch for a breakout confirmation.",
            "Double bottom pattern mila hai. Support ke paas bullish reversal suggest karta hai; breakout confirmation dekho.");
        return new RuleResult(true, answer, Name);
    }
}
