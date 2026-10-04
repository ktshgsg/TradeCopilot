using System.Globalization;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when the MACD line has just crossed below its signal line (gap &lt; 0.5).</summary>
internal sealed class MacdBearishCrossRule : IRule
{
    /// <inheritdoc />
    public string Name => "macd_bearish_cross";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (indicators is null
            || indicators.Macd is not float macd
            || indicators.MacdSignal is not float signal
            || macd >= signal
            || signal - macd >= 0.5f)
        {
            return null;
        }

        if (!RuleMatch.Mentions(query, "macd", "cross"))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            string.Create(CultureInfo.InvariantCulture, $"MACD ({macd:F2}) just crossed below its signal line ({signal:F2}). Bearish momentum may be building; watch for follow-through selling."),
            string.Create(CultureInfo.InvariantCulture, $"MACD ({macd:F2}) signal line ({signal:F2}) ke neeche cross hua hai. Bearish momentum ban sakta hai; follow-through selling pe nazar rakho."));
        return new RuleResult(true, answer, Name);
    }
}
