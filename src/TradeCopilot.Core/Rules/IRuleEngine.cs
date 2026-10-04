using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules;

/// <summary>
/// Evaluates trading questions against OCR text and detected chart patterns,
/// returning a deterministic rule-based answer when a rule applies.
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Runs the rule chain against the current chart context. The first matching rule wins.
    /// </summary>
    /// <param name="query">User question.</param>
    /// <param name="ocr">Latest OCR result from the chart.</param>
    /// <param name="patterns">Patterns detected in the latest frame.</param>
    /// <returns>
    /// The matching rule's result, or a non-matching result when no rule applies.
    /// This method never throws.
    /// </returns>
    RuleResult Evaluate(string query, OcrResult ocr, IReadOnlyList<PatternDetection> patterns);
}
