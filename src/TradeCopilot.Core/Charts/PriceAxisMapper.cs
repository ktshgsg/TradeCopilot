using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TradeCopilot.Core.Ocr;

namespace TradeCopilot.Core.Charts;

/// <summary>
/// Maps OpenCV pixel Y-coordinates to real price values using OCR-detected
/// y-axis labels (e.g. "45,000", "1.0523").
/// </summary>
public sealed class PriceAxisMapper
{
    private static readonly Regex PriceRegex = CreatePriceRegex();

    /// <summary>Minimum pixel distance between two axis labels for a valid slope.</summary>
    private const float MinYPixelSeparation = 20f;

    /// <summary>Creates the mapper.</summary>
    public PriceAxisMapper(ILogger<PriceAxisMapper> log)
    {
        _log = log;
    }

    private readonly ILogger<PriceAxisMapper> _log;

    /// <summary>
    /// Builds a function that maps pixel Y -> price.
    /// Returns null if fewer than 2 axis labels were found at distinct Y positions,
    /// if the labels are closer than <see cref="MinYPixelSeparation"/> pixels apart,
    /// or if the labels report the same price (flat axis).
    /// </summary>
    public Func<float, float>? BuildMapper(OcrResult ocr, int frameWidth)
    {
        if (ocr is null || ocr.Words is null || ocr.Words.Count == 0)
        {
            _log.LogDebug("OcrResult is empty; cannot build price axis mapper.");
            return null;
        }

        // Step 1: Filter candidates — price-like text at the left or right edge.
        var candidates = new List<(float Price, float Y, string Text)>();
        foreach (var word in ocr.Words)
        {
            if (word.Text is null || !PriceRegex.IsMatch(word.Text))
            {
                continue;
            }

            bool isLeftAxis = word.BoundingBox.X < frameWidth * 0.15;
            bool isRightAxis = word.BoundingBox.X > frameWidth * 0.85;
            if (!isLeftAxis && !isRightAxis)
            {
                continue;
            }

            string digits = word.Text.Replace(",", string.Empty);
            if (!float.TryParse(digits, NumberStyles.Float, CultureInfo.InvariantCulture, out float price))
            {
                _log.LogDebug("Failed to parse price text: {Text}", word.Text);
                continue;
            }

            candidates.Add((price, (float)word.BoundingBox.Y, word.Text));
        }

        if (candidates.Count < 2)
        {
            _log.LogDebug("Only {Count} price labels found; need at least 2.", candidates.Count);
            return null;
        }

        // Step 2a: Forex heuristic — no thousands separators anywhere and sub-10
        // values implies a 4-decimal quote (e.g. "1.0523"); keep the precision.
        bool looksForex = candidates.All(c => !c.Text.Contains(','));
        if (looksForex)
        {
            candidates = candidates
                .Select(c => c.Price < 10
                    ? (MathF.Round(c.Price, 4), c.Y, c.Text)
                    : c)
                .ToList();
        }

        // Step 2b: Volume/indicator filter — if the median label is a large price
        // (> 10000) then sub-1000 values are not axis prices; drop them by taking
        // the median price cluster.
        var sortedPrices = candidates.Select(c => c.Price).OrderBy(p => p).ToList();
        float medianPrice = sortedPrices[sortedPrices.Count / 2];
        if (medianPrice > 10000)
        {
            int before = candidates.Count;
            candidates = candidates.Where(c => c.Price >= 1000).ToList();
            if (candidates.Count < before)
            {
                _log.LogDebug(
                    "Dropped {Dropped} non-axis label(s) via median price cluster filter.",
                    before - candidates.Count);
            }
        }

        // Step 3: at least 2 labels at DIFFERENT Y positions.
        var distinct = candidates.DistinctBy(c => c.Y).ToList();
        if (distinct.Count < 2)
        {
            _log.LogDebug("Only {Count} distinct label Y positions; need at least 2.", distinct.Count);
            return null;
        }

        // Step 4: pick the Y extremes and fit a line through them.
        var byY = distinct.OrderBy(c => c.Y).ToList();
        float y1 = byY[0].Y;
        float y2 = byY[^1].Y;
        float price1 = byY[0].Price;
        float price2 = byY[^1].Price;

        if (MathF.Abs(y2 - y1) < MinYPixelSeparation)
        {
            _log.LogDebug("Y positions too close ({Diff:F0}px); cannot build mapper.", MathF.Abs(y2 - y1));
            return null;
        }

        if (price1 == price2)
        {
            _log.LogDebug("Flat axis labels (same price); cannot build mapper.");
            return null;
        }

        float slope = (price2 - price1) / (y2 - y1);
        float intercept = price1 - slope * y1;
        return y => slope * y + intercept;
    }

    private static Regex CreatePriceRegex()
    {
        // Matches: 45000, 45,000, 1.05, 45,000.50
        return new Regex(@"^[0-9]{1,3}(,[0-9]{3})*(\.[0-9]+)?$", RegexOptions.Compiled);
    }
}
