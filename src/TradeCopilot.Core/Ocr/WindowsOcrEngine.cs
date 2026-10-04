using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace TradeCopilot.Core.Ocr;

/// <summary>
/// OCR backed by the in-box Windows recognizer (<c>Windows.Media.Ocr</c>).
/// Returns <see cref="OcrResult.Empty"/> when no recognizer language is installed.
/// </summary>
public sealed partial class WindowsOcrEngine : IOcrEngine
{
    private const int MaxOcrWidth = 1920;
    private readonly ILogger<WindowsOcrEngine> _log;
    private readonly OcrEngine? _engine;

    /// <summary>Creates an OCR engine, preferring en-US when installed.</summary>
    /// <param name="logger">Logger (defaults to null logger).</param>
    public WindowsOcrEngine(ILogger<WindowsOcrEngine>? logger = null)
    {
        _log = logger ?? NullLogger<WindowsOcrEngine>.Instance;
        IReadOnlyList<Language> installed;
        try
        {
            installed = OcrEngine.AvailableRecognizerLanguages;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Windows OCR engine is not available on this machine.");
            installed = Array.Empty<Language>();
        }

        SupportedLanguages = installed.Select(l => l.LanguageTag).ToArray();
        Language? preferred = null;
        for (int i = 0; i < installed.Count; i++)
        {
            if (installed[i].LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase))
            {
                preferred = installed[i];
                break;
            }
        }

        preferred ??= installed.Count > 0 ? installed[0] : null;
        if (preferred is null)
        {
            _log.LogWarning("No Windows OCR recognizer language is installed; OCR will return empty results.");
            IsAvailable = false;
            LanguageTag = string.Empty;
            _engine = null;
            return;
        }

        try
        {
            _engine = OcrEngine.TryCreateFromLanguage(preferred);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to create Windows OCR engine for {Language}.", preferred.LanguageTag);
            _engine = null;
        }

        if (_engine is null)
        {
            IsAvailable = false;
            LanguageTag = string.Empty;
            return;
        }

        IsAvailable = true;
        LanguageTag = _engine.RecognizerLanguage.LanguageTag;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedLanguages { get; }

    /// <inheritdoc />
    public bool IsAvailable { get; }

    /// <summary>BCP-47 tag of the active recognizer, or empty when unavailable.</summary>
    public string LanguageTag { get; }
}
