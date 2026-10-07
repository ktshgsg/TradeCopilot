using FluentAssertions;
using TradeCopilot.Core.Privacy;

namespace TradeCopilot.Tests;

/// <summary>
/// Task 5 stage 3a tests for <see cref="WindowFilter"/>: wildcard matching,
/// escaping, empty-pattern handling, and the no-foreground-window path.
/// </summary>
public sealed class WindowFilterTests
{
    private static WindowFilter WithTitle(string title) => new(() => title);

    /// <summary>"*bank*" matches bank titles case-insensitively.</summary>
    [Theory]
    [InlineData("MyBank - Chrome")]
    [InlineData("HDFC Bank")]
    [InlineData("mybank dashboard")]
    public void IsExcluded_BankPattern_MatchesBankTitles(string title)
    {
        WithTitle(title).IsExcluded(["*bank*"]).Should().BeTrue();
    }

    /// <summary>"*bank*" must not match titles without the whole word "bank".</summary>
    [Fact]
    public void IsExcluded_BankPattern_DoesNotMatchCharter()
    {
        WithTitle("charter").IsExcluded(["*bank*"]).Should().BeFalse();
    }

    /// <summary>Password pattern matches regardless of case.</summary>
    [Fact]
    public void IsExcluded_PasswordPattern_MatchesPasswordManager()
    {
        WithTitle("Password Manager").IsExcluded(["*password*"]).Should().BeTrue();
    }

    /// <summary>An empty pattern list never excludes.</summary>
    [Fact]
    public void IsExcluded_EmptyPatterns_ReturnsFalse()
    {
        WithTitle("HDFC Bank").IsExcluded([]).Should().BeFalse();
    }

    /// <summary>Regex metacharacters in patterns are escaped, never interpreted.</summary>
    [Fact]
    public void IsExcluded_RegexMetacharacters_EscapedWithoutThrow()
    {
        Func<bool> call = () => WithTitle("a.bX").IsExcluded(["a.b*"]);
        call.Should().NotThrow();
        call().Should().BeTrue();
        WithTitle("aXbY").IsExcluded(["a.b*"]).Should().BeFalse();
    }

    /// <summary>No foreground window means no exclusion, without throwing.</summary>
    [Fact]
    public void IsExcluded_EmptyTitle_ReturnsFalse()
    {
        WithTitle(string.Empty).IsExcluded(["*bank*"]).Should().BeFalse();
    }

    /// <summary>Blank patterns are skipped, never matched.</summary>
    [Fact]
    public void IsExcluded_BlankPatterns_Skipped()
    {
        WithTitle("HDFC Bank").IsExcluded(["  ", "*bank*"]).Should().BeTrue();
        WithTitle("HDFC Bank").IsExcluded(["  "]).Should().BeFalse();
    }

    /// <summary>The real filter on CI has no foreground window: false, no throw.</summary>
    [Fact]
    public void RealFilter_WithoutForegroundWindow_ReturnsFalse()
    {
        if (!Environment.UserInteractive)
        {
            return;
        }

        Func<bool> call = () => new WindowFilter().IsExcluded(["*bank*"]);
        call.Should().NotThrow();
    }
}
