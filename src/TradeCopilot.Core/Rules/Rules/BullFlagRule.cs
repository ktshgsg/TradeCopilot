using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when a bull-flag pattern is detected with confidence above 0.6.</summary>
internal sealed class BullFlagRule : IRule
{
    /// <inheritdoc />
    public string Name => "bull_flag";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (!RuleMatch.HasPattern(patterns, PatternLabels.BullFlag, 0.6f))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            "Bull flag detected. Suggests continuation higher after the pause; wait for a breakout above the flag.",
            "Bull flag pattern mila hai. Pause ke baad uptrend continue hone ka signal; flag ke upar breakout ka wait karo.");
        return new RuleResult(true, answer, Name);
    }
}
