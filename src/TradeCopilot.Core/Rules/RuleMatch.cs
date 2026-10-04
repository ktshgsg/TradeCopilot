using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules;

/// <summary>Shared matching helpers for <see cref="IRule"/> implementations.</summary>
internal static class RuleMatch
{
    /// <summary>True when the query mentions any of the terms (case-insensitive, ordinal).</summary>
    internal static bool Mentions(string? query, params string[] terms)
    {
        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        foreach (string term in terms)
        {
            if (query.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a pattern with the given label exceeds the minimum confidence.</summary>
    internal static bool HasPattern(IReadOnlyList<PatternDetection>? patterns, string label, float minConfidence)
    {
        if (patterns is null)
        {
            return false;
        }

        foreach (PatternDetection pattern in patterns)
        {
            if (pattern.Confidence > minConfidence
                && string.Equals(pattern.Label, label, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Picks the answer template: English queries get <paramref name="en"/>;
    /// Hi and Hinglish queries both get the Hinglish <paramref name="hi"/> template
    /// (documented policy: Devanagari queries are answered in Hinglish).
    /// </summary>
    internal static string Pick(Language language, string en, string hi) =>
        language == Language.En ? en : hi;
}
