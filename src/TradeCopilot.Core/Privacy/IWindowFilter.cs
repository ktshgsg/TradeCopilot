namespace TradeCopilot.Core.Privacy;

/// <summary>Decides whether the foreground window excludes capture.</summary>
public interface IWindowFilter
{
    /// <summary>Returns the foreground window title, or "" when unavailable.</summary>
    /// <returns>The title text.</returns>
    string GetForegroundWindowTitle();

    /// <summary>
    /// Returns true when the foreground window title matches any wildcard pattern.
    /// </summary>
    /// <param name="patterns">Wildcard patterns (e.g. "*bank*").</param>
    /// <returns>True when capture must be skipped.</returns>
    bool IsExcluded(IReadOnlyList<string> patterns);
}
