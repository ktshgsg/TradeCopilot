using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Rules.Rules;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Rules;

/// <summary>
/// Rule-chain engine: parses indicators once, then evaluates rules in order until one
/// matches. Rule failures are logged and skipped, so <see cref="Evaluate"/> never throws.
/// </summary>
public sealed class TradingRuleEngine : IRuleEngine
{
    private readonly IReadOnlyList<IRule> _rules;
    private readonly ILogger<TradingRuleEngine> _log;
    private readonly IndicatorParser _parser = new();

    /// <summary>Creates an engine with the built-in rule set.</summary>
    /// <param name="logger">Logger (defaults to null logger).</param>
    public TradingRuleEngine(ILogger<TradingRuleEngine>? logger = null)
        : this(DefaultRules, logger)
    {
    }

    /// <summary>Creates an engine over a custom rule set; the first matching rule wins.</summary>
    /// <param name="rules">Rules in evaluation order.</param>
    /// <param name="logger">Logger (defaults to null logger).</param>
    internal TradingRuleEngine(IEnumerable<IRule> rules, ILogger<TradingRuleEngine>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules.ToArray();
        _log = logger ?? NullLogger<TradingRuleEngine>.Instance;
    }

    /// <summary>The built-in rule set in evaluation order.</summary>
    internal static IReadOnlyList<IRule> DefaultRules { get; } =
    [
        new RsiOverboughtRule(),
        new RsiOversoldRule(),
        new MacdBullishCrossRule(),
        new MacdBearishCrossRule(),
        new SupportBreakRule(),
        new ResistanceBreakRule(),
        new HeadAndShouldersRule(),
        new DoubleTopRule(),
        new DoubleBottomRule(),
        new BullFlagRule(),
        new BearFlagRule(),
    ];

    /// <inheritdoc />
    public RuleResult Evaluate(string query, OcrResult ocr, IReadOnlyList<PatternDetection> patterns)
    {
        query ??= string.Empty;
        patterns ??= Array.Empty<PatternDetection>();

        ParsedIndicators indicators;
        try
        {
            indicators = ocr is null ? ParsedIndicators.Empty : _parser.Parse(ocr);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Indicator parsing failed; evaluating rules without indicators.");
            indicators = ParsedIndicators.Empty;
        }

        Language language = LanguageDetector.Detect(query);
        foreach (IRule rule in _rules)
        {
            try
            {
                RuleResult? result = rule.TryEvaluate(query, indicators, patterns, language);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Rule {Rule} failed during evaluation; continuing with the next rule.", rule.Name);
            }
        }

        return RuleResult.NoMatch;
    }
}
