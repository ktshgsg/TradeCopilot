using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when a bear-flag pattern is detected with confidence above 0.6.</summary>
internal sealed class BearFlagRule : IRule
{
    /// <inheritdoc />
    public string Name => "bear_flag";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (!RuleMatch.HasPattern(patterns, PatternLabels.BearFlag, 0.6f))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            "Bear flag detected. Suggests continuation lower after the pause; watch for a breakdown below the flag.",
            "Bear flag pattern mila hai. Pause ke baad downtrend continue hone ka signal; flag ke neeche breakdown pe nazar rakho.");
        return new RuleResult(true, answer, Name);
    }
}
