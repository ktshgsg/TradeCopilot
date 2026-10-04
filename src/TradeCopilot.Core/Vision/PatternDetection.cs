namespace TradeCopilot.Core.Vision;

/// <summary>
/// A detected chart pattern with a label, confidence, and location in frame pixels.
/// </summary>
/// <param name="Label">Pattern name, e.g. <see cref="PatternLabels.BullFlag"/>.</param>
/// <param name="Confidence">Detection confidence in [0, 1].</param>
/// <param name="BoundingBox">Pattern bounds in frame pixels.</param>
public sealed record PatternDetection(string Label, float Confidence, Rect BoundingBox);

/// <summary>
/// Axis-aligned rectangle in frame pixels.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct Rect(float X, float Y, float Width, float Height);

/// <summary>
/// Canonical labels for the 11 chart patterns the YOLO detector recognizes.
/// </summary>
public static class PatternLabels
{
    /// <summary>Head and shoulders reversal pattern.</summary>
    public const string HeadAndShoulders = "head_and_shoulders";

    /// <summary>Inverse head and shoulders reversal pattern.</summary>
    public const string InverseHeadAndShoulders = "inverse_head_and_shoulders";

    /// <summary>Double top reversal pattern.</summary>
    public const string DoubleTop = "double_top";

    /// <summary>Double bottom reversal pattern.</summary>
    public const string DoubleBottom = "double_bottom";

    /// <summary>Ascending triangle continuation pattern.</summary>
    public const string AscendingTriangle = "ascending_triangle";

    /// <summary>Descending triangle continuation pattern.</summary>
    public const string DescendingTriangle = "descending_triangle";

    /// <summary>Symmetrical triangle continuation pattern.</summary>
    public const string SymmetricalTriangle = "symmetrical_triangle";

    /// <summary>Bull flag continuation pattern.</summary>
    public const string BullFlag = "bull_flag";

    /// <summary>Bear flag continuation pattern.</summary>
    public const string BearFlag = "bear_flag";

    /// <summary>Support level breakdown.</summary>
    public const string SupportBreak = "support_break";

    /// <summary>Resistance level breakout.</summary>
    public const string ResistanceBreak = "resistance_break";
}
