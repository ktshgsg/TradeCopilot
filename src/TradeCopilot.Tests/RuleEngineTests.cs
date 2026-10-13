using FluentAssertions;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Rules;
using TradeCopilot.Core.Rules.Rules;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Tests;

/// <summary>
/// Rule engine tests: per-rule behavior, language routing across English,
/// Hinglish, and Devanagari queries, match rate, and robustness to null input.
/// </summary>
/// <remarks>
/// Response-language policy: English queries receive the English template; Hinglish
/// and Devanagari (Hi) queries both receive the Hinglish ("Hi") template, so Hindi
/// questions are always answered in Roman-script Hinglish.
/// </remarks>
public sealed class RuleEngineTests
{
    /// <summary>Substrings that occur only in Hinglish answer templates.</summary>
    private static readonly string[] HinglishMarkers =
    [
        "pe hai", "karo", "dekho", "mila hai", "nazar", "ho gaya", "ka wait", "socho", "suggest karta", "ban sakta",
    ];

    private static readonly IReadOnlyDictionary<string, float> NoExtra = new Dictionary<string, float>(0);

    private static ParsedIndicators Ind(float? rsi = null, float? macd = null, float? signal = null) =>
        new(rsi, macd, signal, null, null, NoExtra);

    private static IReadOnlyList<PatternDetection> Pat(string label, float confidence) =>
        [new PatternDetection(label, confidence, new Rect(0, 0, 10, 10))];

    private static OcrResult Ocr(string text) => new(text, [], "en-US");

    /// <summary>RSI at or above 70 with an RSI/overbought query fires; boundary and guard cases return null.</summary>
    [Fact]
    public void RsiOverboughtRule_FiresAtOrAbove70_WithBothTemplates()
    {
        var rule = new RsiOverboughtRule();

        RuleResult? en = rule.TryEvaluate("RSI overbought?", Ind(rsi: 72f), [], Language.En);
        en.Should().NotBeNull();
        en!.Matched.Should().BeTrue();
        en.RuleName.Should().Be("rsi_overbought");
        en.AnswerText.Should().Contain("72.0").And.Contain("overbought zone").And.Contain("Watch for reversal");
        en.AnswerText.Should().NotContainAny(HinglishMarkers);

        RuleResult? hi = rule.TryEvaluate("RSI 72 pe hai?", Ind(rsi: 70f), [], Language.Hinglish);
        hi!.AnswerText.Should().Contain("pe hai").And.Contain("dekho");

        rule.TryEvaluate("RSI kya hai?", Ind(rsi: 69.9f), [], Language.En).Should().BeNull();
        rule.TryEvaluate("Price kya hai?", Ind(rsi: 80f), [], Language.En).Should().BeNull();
        rule.TryEvaluate(null!, null!, [], Language.En).Should().BeNull();
    }

    /// <summary>RSI at or below 30 with an RSI/oversold query fires; mirror of the overbought rule.</summary>
    [Fact]
    public void RsiOversoldRule_FiresAtOrBelow30_WithBothTemplates()
    {
        var rule = new RsiOversoldRule();

        RuleResult? en = rule.TryEvaluate("Is RSI oversold?", Ind(rsi: 25f), [], Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("rsi_oversold");
        en.AnswerText.Should().Contain("25.0").And.Contain("oversold zone");
        en.AnswerText.Should().NotContainAny(HinglishMarkers);

        RuleResult? hi = rule.TryEvaluate("RSI 25 pe hai?", Ind(rsi: 30f), [], Language.Hi);
        hi!.AnswerText.Should().Contain("pe hai").And.Contain("short");

        rule.TryEvaluate("RSI?", Ind(rsi: 30.1f), [], Language.En).Should().BeNull();
        rule.TryEvaluate("RSI?", Ind(), [], Language.En).Should().BeNull();
    }

    /// <summary>Recent bullish MACD cross (gap &lt; 0.5) fires; stale or inverse crosses return null.</summary>
    [Fact]
    public void MacdBullishCrossRule_FiresOnRecentUpwardCross()
    {
        var rule = new MacdBullishCrossRule();

        RuleResult? en = rule.TryEvaluate("What does the MACD cross mean?", Ind(macd: 0.30f, signal: 0.10f), [], Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("macd_bullish_cross");
        en.AnswerText.Should().Contain("(0.30)").And.Contain("(0.10)").And.Contain("crossed above");

        RuleResult? hi = rule.TryEvaluate("MACD cross hua?", Ind(macd: 0.3f, signal: 0.1f), [], Language.Hinglish);
        hi!.AnswerText.Should().Contain("upar cross").And.Contain("ban sakta");

        rule.TryEvaluate("MACD?", Ind(macd: 1.5f, signal: 0.5f), [], Language.En).Should().BeNull();
        rule.TryEvaluate("MACD?", Ind(macd: 0.1f, signal: 0.3f), [], Language.En).Should().BeNull();
        rule.TryEvaluate("Volume?", Ind(macd: 0.3f, signal: 0.1f), [], Language.En).Should().BeNull();
    }

    /// <summary>Recent bearish MACD cross (gap &lt; 0.5) fires; stale or inverse crosses return null.</summary>
    [Fact]
    public void MacdBearishCrossRule_FiresOnRecentDownwardCross()
    {
        var rule = new MacdBearishCrossRule();

        RuleResult? en = rule.TryEvaluate(
            "MACD crossed below signal, bearish?", Ind(macd: -0.30f, signal: -0.10f), [], Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("macd_bearish_cross");
        en.AnswerText.Should().Contain("(-0.30)").And.Contain("(-0.10)").And.Contain("crossed below");

        RuleResult? hi = rule.TryEvaluate("MACD neeche cross hua?", Ind(macd: -0.3f, signal: -0.1f), [], Language.Hinglish);
        hi!.AnswerText.Should().Contain("neeche cross").And.Contain("ban sakta");

        rule.TryEvaluate("MACD?", Ind(macd: -1.5f, signal: -0.5f), [], Language.En).Should().BeNull();
        rule.TryEvaluate("MACD?", Ind(macd: -0.1f, signal: -0.3f), [], Language.En).Should().BeNull();
    }

    /// <summary>Support break above 0.6 confidence fires with exact templates; 0.6 or other labels return null.</summary>
    [Fact]
    public void SupportBreakRule_FiresAbove06_WithBothTemplates()
    {
        var rule = new SupportBreakRule();

        RuleResult? en = rule.TryEvaluate("What happened?", Ind(), Pat(PatternLabels.SupportBreak, 0.75f), Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("support_break");
        en.AnswerText.Should().Be("Support broken. Bearish signal. Consider short or wait for retest.");

        RuleResult? hi = rule.TryEvaluate("kya hua?", Ind(), Pat(PatternLabels.SupportBreak, 0.75f), Language.Hinglish);
        hi!.AnswerText.Should().Be("Support break ho gaya. Bearish signal. Short consider karo ya retest ka wait karo.");

        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.SupportBreak, 0.6f), Language.En).Should().BeNull();
        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleTop, 0.9f), Language.En).Should().BeNull();
        rule.TryEvaluate("?", Ind(), null!, Language.En).Should().BeNull();
    }

    /// <summary>Resistance break above 0.6 confidence fires; threshold and label guards return null.</summary>
    [Fact]
    public void ResistanceBreakRule_FiresAbove06_WithBothTemplates()
    {
        var rule = new ResistanceBreakRule();

        RuleResult? en = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.ResistanceBreak, 0.8f), Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("resistance_break");
        en.AnswerText.Should().Be("Resistance broken. Bullish signal. Consider long or wait for retest.");

        RuleResult? hi = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.ResistanceBreak, 0.8f), Language.Hinglish);
        hi!.AnswerText.Should().Be("Resistance break ho gaya. Bullish signal. Long consider karo ya retest ka wait karo.");

        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.ResistanceBreak, 0.6f), Language.En).Should().BeNull();
        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.SupportBreak, 0.9f), Language.En).Should().BeNull();
    }

    /// <summary>Head and shoulders fires above 0.55; exactly 0.55 or other labels return null.</summary>
    [Fact]
    public void HeadAndShouldersRule_FiresAbove055_WithBothTemplates()
    {
        var rule = new HeadAndShouldersRule();

        RuleResult? en = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.HeadAndShoulders, 0.56f), Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("head_and_shoulders");
        en.AnswerText.Should().Contain("bearish reversal").And.Contain("neckline");
        en.AnswerText.Should().NotContainAny(HinglishMarkers);

        RuleResult? hi = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.HeadAndShoulders, 0.9f), Language.Hi);
        hi!.AnswerText.Should().ContainAny(HinglishMarkers);

        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.HeadAndShoulders, 0.55f), Language.En).Should().BeNull();
        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleTop, 0.9f), Language.En).Should().BeNull();
    }

    /// <summary>Double top fires above 0.6; exactly 0.6 returns null.</summary>
    [Fact]
    public void DoubleTopRule_FiresAbove06_WithBothTemplates()
    {
        var rule = new DoubleTopRule();

        RuleResult? en = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleTop, 0.72f), Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("double_top");
        en.AnswerText.Should().Contain("bearish reversal").And.Contain("stops");
        en.AnswerText.Should().NotContainAny(HinglishMarkers);

        RuleResult? hi = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleTop, 0.72f), Language.Hinglish);
        hi!.AnswerText.Should().ContainAny(HinglishMarkers);

        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleTop, 0.6f), Language.En).Should().BeNull();
        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleBottom, 0.9f), Language.En).Should().BeNull();
    }

    /// <summary>Double bottom fires above 0.6; exactly 0.6 returns null.</summary>
    [Fact]
    public void DoubleBottomRule_FiresAbove06_WithBothTemplates()
    {
        var rule = new DoubleBottomRule();

        RuleResult? en = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleBottom, 0.65f), Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("double_bottom");
        en.AnswerText.Should().Contain("bullish reversal").And.Contain("breakout");
        en.AnswerText.Should().NotContainAny(HinglishMarkers);

        RuleResult? hi = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleBottom, 0.65f), Language.Hinglish);
        hi!.AnswerText.Should().ContainAny(HinglishMarkers);

        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.DoubleBottom, 0.6f), Language.En).Should().BeNull();
    }

    /// <summary>Bull flag fires above 0.6; exactly 0.6 returns null.</summary>
    [Fact]
    public void BullFlagRule_FiresAbove06_WithBothTemplates()
    {
        var rule = new BullFlagRule();

        RuleResult? en = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.BullFlag, 0.8f), Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("bull_flag");
        en.AnswerText.Should().Contain("continuation higher").And.Contain("breakout");
        en.AnswerText.Should().NotContainAny(HinglishMarkers);

        RuleResult? hi = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.BullFlag, 0.8f), Language.Hinglish);
        hi!.AnswerText.Should().ContainAny(HinglishMarkers);

        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.BullFlag, 0.6f), Language.En).Should().BeNull();
    }

    /// <summary>Bear flag fires above 0.6; exactly 0.6 returns null.</summary>
    [Fact]
    public void BearFlagRule_FiresAbove06_WithBothTemplates()
    {
        var rule = new BearFlagRule();

        RuleResult? en = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.BearFlag, 0.7f), Language.En);
        en.Should().NotBeNull();
        en!.RuleName.Should().Be("bear_flag");
        en.AnswerText.Should().Contain("continuation lower").And.Contain("breakdown");
        en.AnswerText.Should().NotContainAny(HinglishMarkers);

        RuleResult? hi = rule.TryEvaluate("?", Ind(), Pat(PatternLabels.BearFlag, 0.7f), Language.Hinglish);
        hi!.AnswerText.Should().ContainAny(HinglishMarkers);

        rule.TryEvaluate("?", Ind(), Pat(PatternLabels.BearFlag, 0.6f), Language.En).Should().BeNull();
    }

    /// <summary>Devanagari text alone is Hi; Devanagari mixed with Latin is Hinglish; two Latin markers are Hinglish.</summary>
    [Theory]
    [InlineData("", Language.En)]
    [InlineData("What is RSI?", Language.En)]
    [InlineData("only batao", Language.En)]
    [InlineData("The market looks strong today, should I buy?", Language.En)]
    [InlineData("batao kitna hai?", Language.Hinglish)]
    [InlineData("bhai, support break ho gaya, ab kya karo?", Language.Hinglish)]
    [InlineData("MACD neeche cross hua, milega kya?", Language.Hinglish)]
    [InlineData("RSI क्या है?", Language.Hinglish)]
    [InlineData("ओवरबॉट हो गया", Language.Hi)]
    public void LanguageDetector_Classifies(string text, Language expected) =>
        LanguageDetector.Detect(text).Should().Be(expected);

    /// <summary>Roman-script Hindi vocabulary added after the initial 30-word dictionary.</summary>
    public static TheoryData<string> ExpandedVocabulary => BuildExpandedVocabulary();

    private static TheoryData<string> BuildExpandedVocabulary() => new()
    {
        // Prioritized additions: bhai, samajh, lag-family, mile-ga/-gi, ho-ga/-gi, kar-forms.
        "bhai", "samajh", "samjho", "lag", "laga", "lagta", "lagti", "lagte", "lagne",
        "milega", "milegi", "mila", "mili", "hoga", "hogi", "hoge", "hongi", "karne",
        "karta", "karti", "karte", "karna", "karenge", "karunga", "karungi",

        // Copula and auxiliary forms.
        "ho", "hua", "hui", "hue", "hona", "hone", "hota", "hoti", "hote", "rahega",
        "rahegi", "rahunga", "rahungi",

        // Pronouns, possessives and quantifiers.
        "yaar", "mera", "meri", "tera", "teri", "uska", "uski", "unka", "unki", "apna",
        "apni", "apne", "khud", "mujhe", "tujhe", "tumhe", "sab", "sabhi", "sabse",
        "sabko", "kuch", "wala", "wali", "wale", "walo",

        // Connectives and discourse markers.
        "kyunki", "kyun", "kyu", "matlab", "lekin", "magar", "isliye", "toh", "phir",
        "baad", "baat", "aur",

        // Action verbs.
        "dekh", "dekho", "dekha", "dekhna", "dekhta", "dekhti", "soch", "socho", "sochna",
        "bataye", "rakh", "rakho", "rakhna", "chalo", "chal", "chalta", "chalti", "chalna",
        "tod", "toda", "todna", "lagana", "lagao", "banao", "bana", "banega", "banegi",
        "banenge", "khareed", "khareedna", "bech", "beko", "becho", "bikna",

        // Hedges, evaluation, degree and time.
        "acha", "sahi", "galat", "badhiya", "mast", "shandar", "jugaad", "sach", "sachchi",
        "mushkil", "aasan", "shayad", "zarur", "zaroor", "bilkul", "pakka", "pakki", "jaldi",
        "abhi", "ab", "thoda", "thodi", "bahut", "bohot", "zyada", "kam", "waqt",

        // Place, direction, attention and finance nouns.
        "upar", "neeche", "andar", "bahar", "aage", "pichle", "peeche", "samne", "dhyan",
        "dhyaan", "shuru", "khatam", "paisa", "paise", "lakh", "crore", "rupay", "rupaye",
        "dikhata",
    };

    /// <summary>Every added marker counts: alone it stays below the two-marker threshold, paired it is Hinglish.</summary>
    [Theory]
    [MemberData(nameof(ExpandedVocabulary))]
    public void LanguageDetector_CountsExpandedVocabulary(string word)
    {
        LanguageDetector.Detect(word).Should().Be(Language.En);
        LanguageDetector.Detect($"{word} kya?").Should().Be(Language.Hinglish);
    }

    /// <summary>One routing-corpus row: query, OCR text, pattern context, and expectations.</summary>
    public sealed record RoutingCase(
        string Query,
        string OcrText,
        string? PatternLabel,
        float PatternConfidence,
        Language ExpectedLanguage,
        string ExpectedRule);

    /// <summary>30-query corpus: 10 English, 10 Hinglish, 10 Devanagari; each row triggers the expected rule.</summary>
    public static TheoryData<RoutingCase> RoutingCases => BuildRoutingCases();

    private static TheoryData<RoutingCase> BuildRoutingCases() => new()
    {
        // English (10): classify En, answer in English.
        new("RSI is 72, is it overbought?", "RSI: 72", null, 0.7f, Language.En, "rsi_overbought"),
        new("What does the MACD cross mean?", "MACD: 0.30 Signal: 0.10", null, 0.7f, Language.En, "macd_bullish_cross"),
        new("MACD crossed below its signal, is it bearish?", "MACD: 0.10 Signal: 0.30", null, 0.7f, Language.En, "macd_bearish_cross"),
        new("Support broke, should I short?", "", PatternLabels.SupportBreak, 0.75f, Language.En, "support_break"),
        new("Resistance broken, is it bullish?", "", PatternLabels.ResistanceBreak, 0.80f, Language.En, "resistance_break"),
        new("Head and shoulders on the chart?", "", PatternLabels.HeadAndShoulders, 0.70f, Language.En, "head_and_shoulders"),
        new("Is this a double top?", "", PatternLabels.DoubleTop, 0.72f, Language.En, "double_top"),
        new("Double bottom here, can I buy?", "", PatternLabels.DoubleBottom, 0.65f, Language.En, "double_bottom"),
        new("Bull flag formed, what next?", "", PatternLabels.BullFlag, 0.80f, Language.En, "bull_flag"),
        new("Bear flag visible, is it bearish?", "", PatternLabels.BearFlag, 0.70f, Language.En, "bear_flag"),

        // Hinglish (10): Roman-script Hindi markers, classify Hinglish, answer Hinglish.
        new("RSI 72 pe hai, kya karna chahiye?", "RSI: 72", null, 0.7f, Language.Hinglish, "rsi_overbought"),
        new("MACD cross hua hai, kya matlab hai?", "MACD: 0.30 Signal: 0.10", null, 0.7f, Language.Hinglish, "macd_bullish_cross"),
        new("MACD neeche cross kiya hai, kya karun?", "MACD: 0.10 Signal: 0.30", null, 0.7f, Language.Hinglish, "macd_bearish_cross"),
        new("Support break ho gaya, ab kya karo?", "", PatternLabels.SupportBreak, 0.75f, Language.Hinglish, "support_break"),
        new("Resistance break dikh raha hai, kya karo?", "", PatternLabels.ResistanceBreak, 0.80f, Language.Hinglish, "resistance_break"),
        new("Head and shoulders dikh raha hai, kya karo?", "", PatternLabels.HeadAndShoulders, 0.70f, Language.Hinglish, "head_and_shoulders"),
        new("Double top bana hai, kya batao?", "", PatternLabels.DoubleTop, 0.72f, Language.Hinglish, "double_top"),
        new("Double bottom dikh raha hai, kya karo?", "", PatternLabels.DoubleBottom, 0.65f, Language.Hinglish, "double_bottom"),
        new("Bull flag bana hai, kya karo?", "", PatternLabels.BullFlag, 0.80f, Language.Hinglish, "bull_flag"),
        new("Bear flag dikh raha hai, kya karo?", "", PatternLabels.BearFlag, 0.70f, Language.Hinglish, "bear_flag"),

        // Devanagari (10): classify Hi, answer in Hinglish (documented policy).
        new("ओवरबॉट हो गया, क्या करूँ?", "RSI: 72", null, 0.7f, Language.Hi, "rsi_overbought"),
        new("ओवरसोल्ड लग रहा है, क्या करूँ?", "RSI: 25", null, 0.7f, Language.Hi, "rsi_oversold"),
        new("सपोर्ट टूट गया, क्या करूँ?", "", PatternLabels.SupportBreak, 0.75f, Language.Hi, "support_break"),
        new("रेसिस्टेंस टूट गया, क्या करूँ?", "", PatternLabels.ResistanceBreak, 0.80f, Language.Hi, "resistance_break"),
        new("हेड एंड शोल्डर दिख रहा है, क्या करूँ?", "", PatternLabels.HeadAndShoulders, 0.70f, Language.Hi, "head_and_shoulders"),
        new("डबल टॉप बन गया, क्या करूँ?", "", PatternLabels.DoubleTop, 0.72f, Language.Hi, "double_top"),
        new("डबल बॉटम दिख रहा है, क्या करूँ?", "", PatternLabels.DoubleBottom, 0.65f, Language.Hi, "double_bottom"),
        new("बुल फ्लैग बना है, क्या करूँ?", "", PatternLabels.BullFlag, 0.80f, Language.Hi, "bull_flag"),
        new("बियर फ्लैग दिख रहा है, क्या करूँ?", "", PatternLabels.BearFlag, 0.70f, Language.Hi, "bear_flag"),
        new("चार्ट पर क्या दिख रहा है?", "", PatternLabels.ResistanceBreak, 0.65f, Language.Hi, "resistance_break"),
    };

    /// <summary>Every corpus query classifies correctly and the first matching rule answers in the policy language.</summary>
    [Theory]
    [MemberData(nameof(RoutingCases))]
    public void Evaluate_RoutesQueryAndAnswersInPolicyLanguage(RoutingCase c)
    {
        LanguageDetector.Detect(c.Query).Should().Be(c.ExpectedLanguage);

        IReadOnlyList<PatternDetection> patterns = c.PatternLabel is null
            ? Array.Empty<PatternDetection>()
            : Pat(c.PatternLabel, c.PatternConfidence);
        RuleResult result = new TradingRuleEngine().Evaluate(c.Query, Ocr(c.OcrText), patterns);

        result.Matched.Should().BeTrue();
        result.RuleName.Should().Be(c.ExpectedRule);
        if (c.ExpectedLanguage == Language.En)
        {
            result.AnswerText.Should().NotContainAny(HinglishMarkers);
        }
        else
        {
            result.AnswerText.Should().ContainAny(HinglishMarkers);
        }
    }

    /// <summary>The routing corpus matches at or above the required 60% rate.</summary>
    [Fact]
    public void Evaluate_MatchRateOnCorpus_IsAtLeast60Percent()
    {
        var engine = new TradingRuleEngine();
        int total = 0;
        int matched = 0;
        foreach (RoutingCase c in RoutingCases)
        {
            total++;
            IReadOnlyList<PatternDetection> patterns = c.PatternLabel is null
                ? Array.Empty<PatternDetection>()
                : Pat(c.PatternLabel, c.PatternConfidence);
            if (engine.Evaluate(c.Query, Ocr(c.OcrText), patterns).Matched)
            {
                matched++;
            }
        }

        total.Should().Be(30);
        matched.Should().BeGreaterThanOrEqualTo((int)Math.Ceiling(total * 0.6));
    }

    /// <summary>Null OCR, null query, null patterns, and garbage text never throw and never match.</summary>
    [Fact]
    public void Evaluate_NullAndGarbageInputs_NeverThrows()
    {
        var engine = new TradingRuleEngine();

        engine.Evaluate(null!, null!, null!).Matched.Should().BeFalse();

        RuleResult empty = engine.Evaluate(string.Empty, OcrResult.Empty, Array.Empty<PatternDetection>());
        empty.Matched.Should().BeFalse();
        empty.AnswerText.Should().BeEmpty();
        empty.RuleName.Should().BeEmpty();

        RuleResult garbage = engine.Evaluate(
            "!!! ??? 123 \u0900\u0901", Ocr("garbage %%% 999"), Array.Empty<PatternDetection>());
        garbage.Matched.Should().BeFalse();
    }

    /// <summary>Every built-in rule tolerates null indicators/patterns and empty queries without throwing.</summary>
    [Fact]
    public void AllRules_WithNullIndicatorsOrEmptyInputs_NeverThrow()
    {
        foreach (IRule rule in TradingRuleEngine.DefaultRules)
        {
            Exception? nullIndicators = Record.Exception(
                () => rule.TryEvaluate(string.Empty, null!, Array.Empty<PatternDetection>(), Language.En));
            nullIndicators.Should().BeNull($"rule {rule.Name} must tolerate null indicators");

            Exception? nullEverything = Record.Exception(() => rule.TryEvaluate(null!, null!, null!, Language.Hinglish));
            nullEverything.Should().BeNull($"rule {rule.Name} must tolerate null inputs");
        }
    }

    /// <summary>A failing rule is logged and skipped; evaluation continues to the next rule.</summary>
    [Fact]
    public void Evaluate_WhenARuleThrows_SkipsItAndContinues()
    {
        var engine = new TradingRuleEngine([new ThrowingRule(), new RsiOverboughtRule()]);

        RuleResult result = engine.Evaluate("RSI overbought?", Ocr("RSI: 72"), Array.Empty<PatternDetection>());

        result.RuleName.Should().Be("rsi_overbought");
    }

    private sealed class ThrowingRule : IRule
    {
        public string Name => "throwing";

        public RuleResult? TryEvaluate(
            string query,
            ParsedIndicators indicators,
            IReadOnlyList<PatternDetection> patterns,
            Language language) => throw new InvalidOperationException("boom");
    }




}
