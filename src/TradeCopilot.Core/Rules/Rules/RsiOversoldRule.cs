using System.Globalization;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules.Rules;

/// <summary>Fires when RSI is at or below 30 and the query is about RSI or oversold conditions.</summary>
internal sealed class RsiOversoldRule : IRule
{
    /// <inheritdoc />
    public string Name => "rsi_oversold";

    /// <inheritdoc />
    public RuleResult? TryEvaluate(
        string query,
        ParsedIndicators indicators,
        IReadOnlyList<PatternDetection> patterns,
        Language language)
    {
        if (indicators is null || indicators.Rsi is not float rsi || rsi > 30f)
        {
            return null;
        }

        if (!RuleMatch.Mentions(query, "rsi", "oversold", "ओवरसोल्ड"))
        {
            return null;
        }

        string answer = RuleMatch.Pick(
            language,
            string.Create(CultureInfo.InvariantCulture, $"RSI is at {rsi:F1}, oversold zone. Watch for reversal or divergence before adding shorts."),
            string.Create(CultureInfo.InvariantCulture, $"RSI {rsi:F1} pe hai, oversold zone. Reversal ya divergence dekho shorts add karne se pehle."));
        return new RuleResult(true, answer, Name);
    }
}
