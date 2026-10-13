using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using TradeCopilot.Core.Cache;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Orchestrator;
using TradeCopilot.Core.Rules;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests;

/// <summary>
/// Tests for <see cref="AgentOrchestrator"/>: stage ordering (cache, then
/// rules, then the LLM), language routing, fallback behaviour, and fault tolerance.
/// </summary>
public sealed class OrchestratorTests
{
    private static readonly OcrResult SampleOcr = new("RSI: 72", [], "en-US");

    private static CacheLookupResult Hit(string answer, float similarity = 0.95f) =>
        new(true, answer, similarity, true, 1L);

    private static CacheLookupResult Miss() => new(false, null, 0.2f, false, null);

    private static RuleResult RuleHit(string answer) => new(true, answer, "rsi_overbought");

    private static RuleResult RuleMiss() => RuleResult.NoMatch;

    private static AgentOrchestrator Build(
        ISemanticCache cache,
        IRuleEngine rules,
        ILlmEngine llm,
        AgentState? state = null,
        IVisionEngine? vision = null) =>
        new(state ?? new AgentState(), cache, rules, llm, vision ?? DisabledVisionEngine.Instance,
            NullLogger<AgentOrchestrator>.Instance);

    /// <summary>A high-similarity, context-matching cache hit is returned as "cache".</summary>
    [Fact]
    public async Task AnswerAsync_CacheHit_ReturnsCachedAnswer()
    {
        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Hit("Haan, overbought hai."));
        var rules = Substitute.For<IRuleEngine>();
        var llm = Substitute.For<ILlmEngine>();

        // Warm up JIT and the compiled Hinglish regex so the timing assertion below
        // measures the cache path itself, not one-time process startup cost.
        await Build(cache, rules, llm).AnswerAsync("warmup", CancellationToken.None);

        QueryResult result = await Build(cache, rules, llm).AnswerAsync("RSI 72 pe hai?", CancellationToken.None);

        result.Source.Should().Be("cache");
        result.AnswerText.Should().Be("Haan, overbought hai.");
        result.ElapsedMs.Should().BeLessThan(50);
        rules.DidNotReceive().Evaluate(
            Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>());
    }

    /// <summary>A cache hit below the similarity threshold falls through to the rules.</summary>
    [Fact]
    public async Task AnswerAsync_LowSimilarityHit_FallsThroughToRules()
    {
        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Hit("stale answer", 0.4f));
        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(RuleHit("RSI 72 pe hai."));
        var llm = Substitute.For<ILlmEngine>();

        QueryResult result = await Build(cache, rules, llm).AnswerAsync("RSI 72 pe hai?", CancellationToken.None);

        result.Source.Should().Be("rule");
    }

    /// <summary>A matching rule answers and stores exactly once, without calling the LLM.</summary>
    [Fact]
    public async Task AnswerAsync_RuleMatch_AnswersAndStoresOnce()
    {
        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Miss());
        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(RuleHit("RSI 72 pe hai."));
        var llm = Substitute.For<ILlmEngine>();

        QueryResult result = await Build(cache, rules, llm).AnswerAsync("RSI 72 pe hai?", CancellationToken.None);

        result.Source.Should().Be("rule");
        result.AnswerText.Should().Be("RSI 72 pe hai.");
        await cache.Received(1).StoreAsync(
            "RSI 72 pe hai?",
            "RSI 72 pe hai.",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
        await llm.DidNotReceive().CompleteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>With no cache hit and no rule, the LLM answers and the answer is cached.</summary>
    [Fact]
    public async Task AnswerAsync_LlmPath_AnswersAndStores()
    {
        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Miss());
        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(RuleMiss());
        var llm = Substitute.For<ILlmEngine>();
        llm.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("RSI suggests reversal soon.");

        QueryResult result = await Build(cache, rules, llm).AnswerAsync("What next?", CancellationToken.None);

        result.Source.Should().Be("llm");
        result.AnswerText.Should().Be("RSI suggests reversal soon.");
        await cache.Received(1).StoreAsync(
            "What next?",
            "RSI suggests reversal soon.",
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>An unavailable LLM yields the fallback and caches nothing.</summary>
    [Fact]
    public async Task AnswerAsync_ModelUnavailable_ReturnsFallbackWithoutCaching()
    {
        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Miss());
        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(RuleMiss());
        var llm = Substitute.For<ILlmEngine>();
        llm.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(LlamaSharpEngine.NotAvailableMessage);

        QueryResult result = await Build(cache, rules, llm).AnswerAsync("What next?", CancellationToken.None);

        result.Source.Should().Be("llm");
        result.AnswerText.Should().Contain("Sorry");
        await cache.DidNotReceive().StoreAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An empty or whitespace-only query short-circuits before any stage runs.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnswerAsync_EmptyQuery_ReturnsEmptyWithoutCallingStages(string query)
    {
        var cache = Substitute.For<ISemanticCache>();
        var rules = Substitute.For<IRuleEngine>();
        var llm = Substitute.For<ILlmEngine>();

        QueryResult result = await Build(cache, rules, llm).AnswerAsync(query, CancellationToken.None);

        result.Source.Should().Be("empty");
        result.AnswerText.Should().BeEmpty();
        result.LanguageCode.Should().Be("en");
        await cache.DidNotReceive().QueryAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        rules.DidNotReceive().Evaluate(
            Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>());
        await llm.DidNotReceive().CompleteAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A failing cache still reaches the rule engine; a failing rule still reaches the LLM.</summary>
    [Fact]
    public async Task AnswerAsync_StageFailures_FallThroughToTheNextStage()
    {
        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<CacheLookupResult>(_ => throw new InvalidOperationException("cache down"));
        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(_ => throw new InvalidOperationException("rule exploded"));
        var llm = Substitute.For<ILlmEngine>();
        llm.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("recovered answer");

        QueryResult result = await Build(cache, rules, llm).AnswerAsync("What next?", CancellationToken.None);

        result.Source.Should().Be("llm");
        result.AnswerText.Should().Be("recovered answer");
    }

    /// <summary>A throwing LLM yields the fallback instead of propagating.</summary>
    [Fact]
    public async Task AnswerAsync_LlmThrows_ReturnsFallbackAndNeverRethrows()
    {
        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Miss());
        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(RuleMiss());
        var llm = Substitute.For<ILlmEngine>();
        llm.CompleteAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("llm down"));

        QueryResult result = await Build(cache, rules, llm).AnswerAsync("What next?", CancellationToken.None);

        result.Source.Should().Be("llm");
        result.AnswerText.Should().Contain("Sorry");
    }

    /// <summary>A cache write failure is swallowed: the computed rule answer still returns.</summary>
    [Fact]
    public async Task AnswerAsync_StoreFails_StillReturnsRuleAnswer()
    {
        var cache = Substitute.For<ISemanticCache>();
        cache.QueryAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Miss());
        cache.StoreAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new InvalidOperationException("disk full"));
        var rules = Substitute.For<IRuleEngine>();
        rules.Evaluate(Arg.Any<string>(), Arg.Any<OcrResult>(), Arg.Any<IReadOnlyList<PatternDetection>>())
            .Returns(RuleHit("RSI 72 pe hai."));

        QueryResult result = await Build(cache, rules, Substitute.For<ILlmEngine>())
            .AnswerAsync("RSI 72 pe hai?", CancellationToken.None);

        result.Source.Should().Be("rule");
        result.AnswerText.Should().Be("RSI 72 pe hai.");
    }
}
