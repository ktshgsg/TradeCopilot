using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using TradeCopilot.Core.Ocr;
using TradeCopilot.Core.Vision;

namespace TradeCopilot.Core.Cache;

/// <summary>
/// Stable 16-char fingerprint of the chart context (OCR text + pattern labels), used as a cache key.
/// </summary>
public static class ChartContextHash
{
    /// <summary>
    /// Lowercases OCR text, collapses whitespace, strips punctuation, appends sorted
    /// "label:conf_bucket" entries (0.1 resolution), then returns truncated SHA256 hex.
    /// </summary>
    /// <param name="ocr">Recognized frame text.</param>
    /// <param name="patterns">Detected patterns.</param>
    /// <returns>16 lowercase hex characters.</returns>
    public static string Compute(OcrResult ocr, IReadOnlyList<PatternDetection> patterns)
    {
        ArgumentNullException.ThrowIfNull(ocr);
        ArgumentNullException.ThrowIfNull(patterns);

        var sb = new StringBuilder(ocr.FullText.Length);
        bool lastSpace = true;
        foreach (char c in ocr.FullText.ToLowerInvariant())
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace)
                {
                    sb.Append(' ');
                    lastSpace = true;
                }
            }
            else if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                lastSpace = false;
            }
        }

        string text = sb.ToString().Trim();
        IEnumerable<string> parts = patterns
            .Select(p => string.Create(
                CultureInfo.InvariantCulture,
                $"{p.Label.ToLowerInvariant()}:{(int)Math.Floor(p.Confidence * 10.0)}"))
            .OrderBy(s => s, StringComparer.Ordinal);
        string canonical = string.Create(CultureInfo.InvariantCulture, $"{text}|{string.Join(",", parts)}");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
