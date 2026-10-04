using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when a head-and-shoulders pattern is detected with confidence above 0.55.</summary>
internal sealed class HeadAndShouldersRule : IRule
{
    /// <inheritdoc />
    public string Name => "head_and_shoulders";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (!RuleMatch.HasPattern(patterns, PatternLabels.HeadAndShoulders, 0.55f))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            "Head and shoulders detected. This may mark a bearish reversal; wait for the neckline break to confirm.",
            "Head and shoulders pattern mila hai. Ye bearish reversal ho sakta hai; neckline break ke confirmation ka wait karo.");
        return new RuleResult(true, answer, Name);
    }
}
