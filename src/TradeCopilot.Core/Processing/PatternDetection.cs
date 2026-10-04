namespace TradeCopilot.Core.Processing;

/// <summary>
/// A detected chart pattern. The detector arrives in Task 2; producers return empty lists for now.
/// </summary>
/// <param name="Label">Pattern name, e.g. "bull_flag".</param>
/// <param name="Confidence">Detection confidence in [0, 1].</param>
public sealed record PatternDetection(string Label, double Confidence);
