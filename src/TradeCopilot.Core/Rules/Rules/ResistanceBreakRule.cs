using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when a resistance-break pattern is detected with confidence above 0.6.</summary>
internal sealed class ResistanceBreakRule : IRule
{
    /// <inheritdoc />
    public string Name => "resistance_break";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (!RuleMatch.HasPattern(patterns, PatternLabels.ResistanceBreak, 0.6f))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            "Resistance broken. Bullish signal. Consider long or wait for retest.",
            "Resistance break ho gaya. Bullish signal. Long consider karo ya retest ka wait karo.");
        return new RuleResult(true, answer, Name);
    }
}
