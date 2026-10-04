using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when a support-break pattern is detected with confidence above 0.6.</summary>
internal sealed class SupportBreakRule : IRule
{
    /// <inheritdoc />
    public string Name => "support_break";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (!RuleMatch.HasPattern(patterns, PatternLabels.SupportBreak, 0.6f))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            "Support broken. Bearish signal. Consider short or wait for retest.",
            "Support break ho gaya. Bearish signal. Short consider karo ya retest ka wait karo.");
        return new RuleResult(true, answer, Name);
    }
}
