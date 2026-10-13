using FluentAssertions;
using TradeCopilot.Core.Privacy;

namespace TradeCopilot.Tests;

/// <summary>
/// Tests for <see cref="SensitiveDetector"/>: the 14-row
/// null/empty/whitespace truth table, per-keyword positives, word-boundary
/// negatives, and the no-throw contract.
/// </summary>
public sealed class SensitiveDetectorTests
{
    /// <summary>Every null/empty/whitespace + signal combination in the spec.</summary>
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(null, "", false)]
    [InlineData(null, "   ", false)]
    [InlineData("", null, false)]
    [InlineData("   ", null, false)]
    [InlineData("", "", false)]
    [InlineData("   ", "   ", false)]
    [InlineData(null, "HDFC Bank", true)]
    [InlineData("Enter password", null, true)]
    [InlineData("Enter password", "", true)]
    [InlineData("RSI 55 trend up", "TradingView", false)]
    [InlineData("RSI 55 trend up", "HDFC Bank", true)]
    [InlineData("OTP डालो", "Chrome", true)]
    [InlineData("support break ho gaya", "TradingView", false)]
    public void IsSensitive_TruthTable(string? ocrText, string? windowTitle, bool expected)
    {
        SensitiveDetector.IsSensitive(ocrText, windowTitle).Should().Be(expected);
    }

    /// <summary>Each OCR keyword fires inside a realistic sentence.</summary>
    [Theory]
    [InlineData("Your password is required")]
    [InlineData("Enter the OTP sent to your phone")]
    [InlineData("CVV is on the back of the card")]
    [InlineData("Your PIN is required")]
    [InlineData("Net banking is disabled")]
    [InlineData("Use net banking to pay")]
    [InlineData("Use net  banking to pay")] // double space: net\s+banking
    [InlineData("Login failed")]
    [InlineData("Update your account number")]
    [InlineData("Aadhaar verification")]
    [InlineData("PAN card update")]
    [InlineData("IFSC code required")]
    [InlineData("Pay via UPI")]
    [InlineData("netbanking services are down")]
    public void IsSensitive_PositiveOcrKeywords(string ocrText)
    {
        SensitiveDetector.IsSensitive(ocrText, "TradingView - Chart").Should().BeTrue();
    }

    /// <summary>Each title keyword fires on the window side.</summary>
    [Theory]
    [InlineData("HDFC Bank - Chrome")]
    [InlineData("My Wallet App")]
    [InlineData("Password Manager")]
    [InlineData("Keychain Access")]
    [InlineData("Netbanking Portal")]
    [InlineData("Login - MyTradeApp")]
    [InlineData("Signin Required")]
    public void IsSensitive_PositiveTitleKeywords(string windowTitle)
    {
        SensitiveDetector.IsSensitive("RSI 55 trend up", windowTitle).Should().BeTrue();
    }

    /// <summary>Benign trading text and near-miss substrings must not match.</summary>
    [Theory]
    [InlineData("trend analysis chart")]
    [InlineData("support break ho gaya")]
    [InlineData("RSI is overbought")]
    [InlineData("the band played music")]
    [InlineData("This is your P I N")]
    [InlineData("panthers are winning")] // "pan" inside "panthers" is one word: \b blocks it
    [InlineData("ship in a bottle")] // "ship"/"in" are separate words, never "pin"
    [InlineData("charter of accounts")] // "*bank*" wildcard concern: "bank" is not in "charter"
    public void IsSensitive_NegativeOcrCases(string ocrText)
    {
        SensitiveDetector.IsSensitive(ocrText, "TradingView - Chart").Should().BeFalse();
    }

    /// <summary>
    /// Latin-only title keywords cannot match Devanagari titles. The window title
    /// for Indian banking apps is almost always English, so this is acceptable for v1.
    /// </summary>
    [Fact]
    public void IsSensitive_DevanagariTitle_ReturnsFalse()
    {
        SensitiveDetector.IsSensitive("RSI 55 trend up", "HDFC बैंक").Should().BeFalse();
    }

    /// <summary>Never throws, including for every truth-table combination.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData(null, "")]
    [InlineData(null, "   ")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("", "")]
    [InlineData("   ", "   ")]
    [InlineData(null, "HDFC Bank")]
    [InlineData("Enter password", null)]
    [InlineData("Enter password", "")]
    [InlineData("RSI 55 trend up", "TradingView")]
    [InlineData("RSI 55 trend up", "HDFC Bank")]
    [InlineData("OTP डालो", "Chrome")]
    [InlineData("support break ho gaya", "TradingView")]
    public void IsSensitive_NeverThrows(string? ocrText, string? windowTitle)
    {
        Func<bool> call = () => SensitiveDetector.IsSensitive(ocrText, windowTitle);
        call.Should().NotThrow();
    }
}
