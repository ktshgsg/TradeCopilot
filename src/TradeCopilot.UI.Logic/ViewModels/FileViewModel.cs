using CommunityToolkit.Mvvm.ComponentModel;
using TradeCopilot.Core.Setup;

namespace TradeCopilot.UI.Logic.ViewModels;

/// <summary>
/// One download row in the first-run wizard: bound name, target path, status icon, and
/// progress bar. Mutable state (bytes/total/speed) backs the wizard's overall progress.
/// </summary>
public sealed partial class FileViewModel : ObservableObject
{
    /// <summary>Creates the row for one manifest entry.</summary>
    /// <param name="file">Manifest entry being downloaded.</param>
    public FileViewModel(ModelFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        Model = file;
        Name = file.Name;
        TargetPath = file.TargetPath;
        Total = file.ExpectedSize;
        DetailText = $"0 / {FormatSize(file.ExpectedSize)}";
    }

    /// <summary>Manifest entry for this row.</summary>
    public ModelFile Model { get; }

    /// <summary>Display name from the manifest.</summary>
    public string Name { get; }

    /// <summary>Target path from the manifest (relative to the app root).</summary>
    public string TargetPath { get; }

    /// <summary>Human-readable status: Pending, Queued, Downloading, Complete, Failed, or Canceled.</summary>
    [ObservableProperty]
    private string _statusText = "Pending";

    /// <summary>Status glyph: "…" pending, "↓" downloading, "✓" complete, "✗" failed, "‖" canceled.</summary>
    [ObservableProperty]
    private string _statusIcon = "\u2026";

    /// <summary>Status color as a hex brush string (gray, blue, green, or red).</summary>
    [ObservableProperty]
    private string _statusBrush = "#888888";

    /// <summary>Progress percentage 0-100.</summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>Detail line: "X / Y · Z/s".</summary>
    [ObservableProperty]
    private string _detailText;

    /// <summary>True once the file is on disk (or already was).</summary>
    [ObservableProperty]
    private bool _isComplete;

    /// <summary>Bytes reported so far, including a resumed prefix (wizard total state).</summary>
    internal long Received { get; set; }

    /// <summary>Total expected bytes for this file (0 when unknown).</summary>
    internal long Total { get; set; }

    /// <summary>Latest throughput report in bytes per second.</summary>
    internal double Speed { get; set; }

    /// <summary>Marks the row complete and pins the progress bar at 100%.</summary>
    /// <param name="bytes">Final file size in bytes.</param>
    internal void MarkComplete(long bytes)
    {
        Received = bytes;
        Total = bytes;
        Speed = 0;
        IsComplete = true;
        StatusText = "Complete";
        StatusIcon = "\u2713";
        StatusBrush = "#4CAF50";
        Progress = 100;
        DetailText = $"{FormatSize(bytes)} / {FormatSize(bytes)}";
    }

    /// <summary>Sets the status text, icon, and color together.</summary>
    /// <param name="text">Status text.</param>
    /// <param name="icon">Status glyph.</param>
    /// <param name="brush">Status color as a hex string.</param>
    internal void SetStatus(string text, string icon, string brush)
    {
        StatusText = text;
        StatusIcon = icon;
        StatusBrush = brush;
    }

    /// <summary>Updates the progress bar and detail line.</summary>
    /// <param name="percent">Progress percentage 0-100.</param>
    /// <param name="detail">Detail line text.</param>
    internal void SetProgress(double percent, string detail)
    {
        Progress = percent;
        DetailText = detail;
    }

    /// <summary>Formats a byte count with a human-readable unit (B, KB, MB, or GB).</summary>
    /// <param name="bytes">Byte count.</param>
    /// <returns>e.g. "1176.0 MB".</returns>
    internal static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F1} GB",
        >= 1_000_000 => $"{bytes / 1_000_000.0:F1} MB",
        >= 1_000 => $"{bytes / 1_000.0:F0} KB",
        _ => $"{bytes} B",
    };
}