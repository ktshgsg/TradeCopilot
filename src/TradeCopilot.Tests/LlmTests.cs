using System.Diagnostics;
using FluentAssertions;
using TradeCopilot.Core.Llm;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Vision;
using Xunit.Abstractions;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 3 stage 2 tests: LLamaSharp engine behavior with and without a model,
/// prompt building, and the Qwen2.5 chat template.
/// </summary>
public sealed class LlmTests
{
    private const string ModelFileName = "bharat-tiny-llm-v3-q4_k_m.gguf";

    private static readonly string[] NumberMarkers = ["72", "overbought"];
    private static readonly string[] HinglishResponseMarkers = ["hai", "karo", "dekho", "zone"];

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class with xUnit's output helper for timing logs.</summary>
    public LlmTests(ITestOutputHelper output) => _output = output;

    /// <summary>Finds the repo's models/ directory by walking up from the test output folder.</summary>
    internal static string FindModelPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "models", ModelFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "models", ModelFileName);
    }

    /// <summary>Skips model-dependent tests with a clear reason when the GGUF file is absent.</summary>
    internal sealed class RequiresModelFactAttribute : FactAttribute
    {
        public RequiresModelFactAttribute()
        {
            string path = FindModelPath();
            if (!File.Exists(path))
            {
                Skip = $"LLM model not present at {path}; download Bharat-Tiny-LLM v3 from "
                    + "https://huggingface.co/eulogik/Bharat-Tiny-LLM-v3 (see models/README.md).";
            }
        }
    }

    /// <summary>A missing model file leaves the engine disabled and answering with the sentinel.</summary>
    [Fact]
    public async Task Engine_WhenModelMissing_StaysDisabled_AndReturnsNotAvailable()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"tc-missing-{Guid.NewGuid():N}.gguf");
        using var engine = new LlamaSharpEngine();

        await engine.InitializeAsync(missing, CancellationToken.None);

        engine.Enabled.Should().BeFalse();
        string completion = await engine.CompleteAsync("system", "user", CancellationToken.None);
        completion.Should().Be("Model not available.");

        var tokens = new List<string>();
        await foreach (string token in engine.StreamAsync("system", "user", CancellationToken.None))
        {
            tokens.Add(token);
        }

        tokens.Should().ContainSingle().Which.Should().Be("Model not available.");
    }

    /// <summary>OCR longer than 1500 codepoints is truncated by codepoint count.</summary>
    [Fact]
    public void BuildUserPrompt_TruncatesOcrAt1500Codepoints()
    {
        string longText = new('क', 1540);
        string prompt = PromptBuilder.BuildUserPrompt("kya hai?", new OcrResult(longText, [], "hi-IN"), [], null);

        prompt.Should().Contain(new string('क', 1500));
        prompt.Should().NotContain(new string('क', 1501));
        prompt.Should().Contain("[CHART CONTEXT]").And.Contain("[USER QUERY]\nkya hai?");
    }

    /// <summary>Empty pattern list and missing cache render as "none".</summary>
    [Fact]
    public void BuildUserPrompt_EmptyPatternsAndCache_RenderNone()
    {
        string prompt = PromptBuilder.BuildUserPrompt("RSI?", new OcrResult("RSI: 55", [], "en-US"), [], null);

        prompt.Should().Contain("OCR: RSI: 55");
        prompt.Should().Contain("Patterns: none");
        prompt.Should().Contain("Cached: none");
        prompt.Should().Contain("[USER QUERY]\nRSI?");
    }

    /// <summary>Patterns render as "label (confidence)" pairs; cached context passes through.</summary>
    [Fact]
    public void BuildUserPrompt_WithPatternsAndCache_RendersValues()
    {
        string prompt = PromptBuilder.BuildUserPrompt(
            "kya scene hai?",
            new OcrResult("RSI: 72", [], "hi-IN"),
            [
                new PatternDetection(PatternLabels.BullFlag, 0.72f, new Rect(0, 0, 10, 10)),
                new PatternDetection(PatternLabels.DoubleTop, 0.50f, new Rect(0, 0, 10, 10)),
            ],
            "RSI 72 pe hai, overbought zone.");

        prompt.Should().Contain("Patterns: bull_flag (0.72), double_top (0.50)");
        prompt.Should().Contain("Cached: RSI 72 pe hai, overbought zone.");
    }

    /// <summary>Null OCR is tolerated and no output line carries trailing whitespace.</summary>
    [Fact]
    public void BuildUserPrompt_NullOcr_HasNoTrailingWhitespace()
    {
        string prompt = PromptBuilder.BuildUserPrompt("q", null, [], "");

        prompt.Should().Contain("OCR:").And.Contain("Cached: none");
        prompt.Split('\n').Should().AllSatisfy(line => line.Should().Be(line.TrimEnd()));
    }

    /// <summary>The Qwen2.5 chat template matches the specification character for character.</summary>
    [Fact]
    public void BuildChatPrompt_MatchesQwen25Template_CharForChar()
    {
        const string expected =
            "<|im_start|>system\nYou are helpful.<|im_end|>\n"
            + "<|im_start|>user\nWhat is RSI?<|im_end|>\n"
            + "<|im_start|>assistant\n";

        PromptBuilder.BuildChatPrompt("You are helpful.", "What is RSI?").Should().Be(expected);
    }

    /// <summary>The system prompt matches the specification character for character.</summary>
    [Fact]
    public void BuildSystemPrompt_MatchesSpec()
    {
        const string expected =
            "You are TradeCopilot, an expert trading assistant for Indian retail traders.\n\n"
            + "RULES:\n"
            + "1. Respond in the SAME language AND script the user asked in.\n"
            + "   - English query -> English response (Latin script).\n"
            + "   - Devanagari Hindi/Hinglish query -> Devanagari response.\n"
            + "   - Roman-script Hinglish query -> Roman-script Hinglish response.\n"
            + "   Technical terms (RSI, MACD, support, resistance, breakout) stay in\n"
            + "   Latin script in all cases.\n"
            + "2. Keep answers under 3 sentences unless asked for detail.\n"
            + "3. Cite exact numbers from the chart context.\n"
            + "4. Never invent indicator values. If not provided, say you can't see it.\n"
            + "5. Use 'may', 'suggests', 'likely'. Never give certainty.\n"
            + "6. Never give financial advice as a promise of returns.";

        PromptBuilder.BuildSystemPrompt().Should().Be(expected);
    }

    /// <summary>The system prompt requires matching both language and script, per Task 4.</summary>
    [Fact]
    public void BuildSystemPrompt_RequiresSameLanguageAndScript()
    {
        string prompt = PromptBuilder.BuildSystemPrompt();

        prompt.Should().Contain("SAME language AND script");
        prompt.Should().Contain("English query -> English response (Latin script).");
        prompt.Should().Contain("Devanagari Hindi/Hinglish query -> Devanagari response.");
        prompt.Should().Contain("Roman-script Hinglish query -> Roman-script Hinglish response.");
        prompt.Should().Contain("Technical terms (RSI, MACD, support, resistance, breakout) stay in");
        prompt.Should().NotContain("Roman script preferred");
    }

    /// <summary>With the model present, a Hinglish query yields numbers plus Hinglish markers.</summary>
    [RequiresModelFact]
    [Trait("Category", "RequiresModel")]
    public async Task Model_HinglishQuery_AnswersWithNumbersAndHinglish()
    {
        using var engine = new LlamaSharpEngine();
        await engine.InitializeAsync(FindModelPath(), CancellationToken.None);
        engine.Enabled.Should().BeTrue();

        string answer = await engine.CompleteAsync(
            PromptBuilder.BuildSystemPrompt(),
            PromptBuilder.BuildUserPrompt(
                "RSI 72 pe hai, kya karna chahiye?", new OcrResult("RSI: 72", [], "hi-IN"), [], null),
            CancellationToken.None);

        _output.WriteLine($"Hinglish answer: {answer}");
        answer.Should().NotBeNullOrWhiteSpace();
        answer.ToLowerInvariant().Should().ContainAny(NumberMarkers);
        answer.ToLowerInvariant().Should().ContainAny(HinglishResponseMarkers);
    }

    /// <summary>With the model present, an English query is answered without Devanagari script.</summary>
    [RequiresModelFact]
    [Trait("Category", "RequiresModel")]
    public async Task Model_EnglishQuery_AnswersInEnglish()
    {
        using var engine = new LlamaSharpEngine();
        await engine.InitializeAsync(FindModelPath(), CancellationToken.None);

        string answer = await engine.CompleteAsync(
            PromptBuilder.BuildSystemPrompt(),
            PromptBuilder.BuildUserPrompt("What is RSI?", new OcrResult("RSI: 55", [], "en-US"), [], null),
            CancellationToken.None);

        _output.WriteLine($"English answer: {answer}");
        answer.Should().NotBeNullOrWhiteSpace();
        answer.Should().NotMatchRegex("[\u0900-\u097F]");
    }

    /// <summary>Measures 50 generated tokens; slow hardware logs a warning but never fails the test.</summary>
    [RequiresModelFact]
    [Trait("Category", "RequiresModel")]
    public async Task Model_FiftyTokenCompletion_LogsTiming()
    {
        using var engine = new LlamaSharpEngine();
        await engine.InitializeAsync(FindModelPath(), CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        int tokens = 0;
        await foreach (string _ in engine.StreamAsync(
            PromptBuilder.BuildSystemPrompt(),
            PromptBuilder.BuildUserPrompt(
                "Reply with one short sentence about RSI.", new OcrResult("RSI: 55", [], "en-US"), [], null),
            CancellationToken.None))
        {
            tokens++;
            if (tokens >= 50)
            {
                break;
            }
        }

        stopwatch.Stop();
        tokens.Should().BeGreaterThan(0);
        _output.WriteLine($"50-token completion took {stopwatch.ElapsedMilliseconds} ms on CPU ({tokens} tokens).");
        if (stopwatch.ElapsedMilliseconds > 3000)
        {
            _output.WriteLine("WARNING: 50-token completion exceeded the 3s CPU budget; hardware may be slow.");
        }
    }

    /// <summary>Streaming yields at least five tokens before completing.</summary>
    [RequiresModelFact]
    [Trait("Category", "RequiresModel")]
    public async Task Model_Streaming_YieldsAtLeastFiveTokens()
    {
        using var engine = new LlamaSharpEngine();
        await engine.InitializeAsync(FindModelPath(), CancellationToken.None);

        int count = 0;
        await foreach (string token in engine.StreamAsync(
            PromptBuilder.BuildSystemPrompt(),
            PromptBuilder.BuildUserPrompt("What is RSI?", new OcrResult("RSI: 55", [], "en-US"), [], null),
            CancellationToken.None))
        {
            token.Should().NotBeNull();
            count++;
            if (count >= 5)
            {
                break;
            }
        }

        count.Should().BeGreaterThanOrEqualTo(5);
    }

}
