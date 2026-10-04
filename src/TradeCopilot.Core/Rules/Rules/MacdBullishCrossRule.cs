using System.Globalization;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when the MACD line has just crossed above its signal line (gap &lt; 0.5).</summary>
internal sealed class MacdBullishCrossRule : IRule
{
    /// <inheritdoc />
    public string Name => "macd_bullish_cross";

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
            || macd <= signal
            || macd - signal >= 0.5f)
        {
            return null;
        }

        if (!RuleMatch.Mentions(query, "macd", "cross"))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            string.Create(CultureInfo.InvariantCulture, $"MACD ({macd:F2}) just crossed above its signal line ({signal:F2}). Bullish momentum may be building; wait for a volume-backed follow-through."),
            string.Create(CultureInfo.InvariantCulture, $"MACD ({macd:F2}) signal line ({signal:F2}) ke upar cross hua hai. Bullish momentum ban sakta hai; volume ke saath follow-through ka wait karo."));
        return new RuleResult(true, answer, Name);
    }
}
