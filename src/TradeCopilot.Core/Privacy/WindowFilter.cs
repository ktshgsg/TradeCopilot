using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace TradeCopilot.Core.Privacy;

/// <summary>
/// Matches the foreground window title against wildcard exclusion patterns.
/// </summary>
/// <remarks>
/// P/Invoke is isolated and never throws: failures yield "" (title) or false
/// (excluded). Compiled patterns are cached process-lifetime in a
/// <see cref="ConcurrentDictionary{TKey, TValue}"/> bounded by config size.
/// </remarks>
public partial class WindowFilter : IWindowFilter
{
    private const int TitleBufferChars = 512;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new(StringComparer.Ordinal);

    private static readonly Regex NeverMatches = new("^$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly Func<string> _titleProvider;

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll", EntryPoint = "GetWindowTextW")]
    private static unsafe partial int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);

    /// <summary>Creates a filter that reads the real foreground window.</summary>
    public WindowFilter()
        : this(null)
    {
    }

    /// <summary>Creates a filter with an injected title source (for tests).</summary>
    /// <param name="titleProvider">Title source; null uses the foreground window.</param>
    internal WindowFilter(Func<string>? titleProvider)
    {
        _titleProvider = titleProvider ?? ReadForegroundWindowTitle;
    }

    /// <inheritdoc />
    public string GetForegroundWindowTitle()
    {
        try
        {
            return _titleProvider() ?? string.Empty;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return string.Empty;
        }
    }

    private static string ReadForegroundWindowTitle()
    {
        try
        {
            nint hwnd = GetForegroundWindow();
            if (hwnd == 0)
            {
                return string.Empty;
            }

            Span<char> buffer = stackalloc char[TitleBufferChars];
            int length;
            unsafe
            {
                fixed (char* p = buffer)
                {
                    length = GetWindowTextW(hwnd, p, buffer.Length);
                }
            }

            return length <= 0 ? string.Empty : new string(buffer[..length]);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public bool IsExcluded(IReadOnlyList<string> patterns)
    {
        if (patterns is null || patterns.Count == 0)
        {
            return false;
        }

        try
        {
            string title = GetForegroundWindowTitle();
            if (string.IsNullOrEmpty(title))
            {
                return false;
            }

            foreach (string pattern in patterns)
            {
                if (string.IsNullOrWhiteSpace(pattern))
                {
                    continue;
                }

                Regex regex = RegexCache.GetOrAdd(pattern, CompileWildcard);
                if (regex.IsMatch(title))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static Regex CompileWildcard(string pattern)
    {
        try
        {
            return new Regex(
                "^" + Regex.Escape(pattern).Replace(@"\*", ".*", StringComparison.Ordinal) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
        }
        catch (ArgumentException)
        {
            return NeverMatches;
        }
    }
}
