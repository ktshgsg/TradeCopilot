using System.Text.RegularExpressions;

namespace TradeCopilot.Core.Privacy;

/// <summary>Detects sensitive content in OCR text and window titles.</summary>
public static class SensitiveDetector
{
    private static readonly Regex TitleKeywords = new(
        @"\b(bank|wallet|password|keychain|netbanking|login|signin)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex OcrKeywords = new(
        @"\b(password|OTP|CVV|PIN|netbanking|net\s+banking|login|account\s+number|aadhaar|PAN|IFSC|UPI)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns true when OCR text or the window title contains sensitive keywords.
    /// </summary>
    /// <param name="ocrText">Recognized frame text.</param>
    /// <param name="windowTitle">Foreground window title.</param>
    /// <returns>True when the frame must be treated as sensitive.</returns>
    public static bool IsSensitive(string? ocrText, string? windowTitle)
    {
        bool hasTitle = !string.IsNullOrWhiteSpace(windowTitle);
        bool hasOcr = !string.IsNullOrWhiteSpace(ocrText);

        if (!hasTitle && !hasOcr)
        {
            return false;
        }

        if (hasTitle && TitleKeywords.IsMatch(windowTitle!))
        {
            return true;
        }

        if (hasOcr && OcrKeywords.IsMatch(ocrText!))
        {
            return true;
        }

        return false;
    }
}
