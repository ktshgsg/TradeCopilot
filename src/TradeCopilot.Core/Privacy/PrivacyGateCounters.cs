namespace TradeCopilot.Core.Privacy;

/// <summary>
/// Process-wide privacy-gate exclusion counters, shared between the capture
/// pipeline (which increments them) and the status endpoint (which reads them).
/// </summary>
public sealed class PrivacyGateCounters
{
    private long _excludedByWindow;
    private long _excludedBySensitive;

    /// <summary>Frames dropped before OCR because the window title was excluded.</summary>
    public long ExcludedByWindow => Interlocked.Read(ref _excludedByWindow);

    /// <summary>Frames dropped after OCR because the content was sensitive.</summary>
    public long ExcludedBySensitive => Interlocked.Read(ref _excludedBySensitive);

    /// <summary>Increments the window-exclusion counter.</summary>
    public void IncrementWindow() => Interlocked.Increment(ref _excludedByWindow);

    /// <summary>Increments the sensitive-content counter.</summary>
    public void IncrementSensitive() => Interlocked.Increment(ref _excludedBySensitive);
}
