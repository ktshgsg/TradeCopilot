using System.Globalization;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when RSI is at or above 70 and the query is about RSI or overbought conditions.</summary>
internal sealed class RsiOverboughtRule : IRule
{
    /// <inheritdoc />
    public string Name => "rsi_overbought";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (indicators is null || indicators.Rsi is not float rsi || rsi < 70f)
        {
            return null;
        }

        if (!RuleMatch.Mentions(query, "rsi", "overbought", "ओवरबॉट"))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            string.Create(CultureInfo.InvariantCulture, $"RSI is at {rsi:F1}, overbought zone. Watch for reversal or divergence before adding longs."),
            string.Create(CultureInfo.InvariantCulture, $"RSI {rsi:F1} pe hai, overbought zone. Reversal ya divergence dekho longs add karne se pehle."));
        return new RuleResult(true, answer, Name);
    }
}
