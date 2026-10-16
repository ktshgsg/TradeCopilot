using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using TradeCopilot.Core.Setup;

namespace TradeCopilot.UI.Logic.ViewModels;

/// <summary>
/// First-run model downloader: one <see cref="FileViewModel"/> row per required manifest
/// entry, at most two downloads in flight, overall progress and ETA, and a flag file so
/// the wizard appears only once. "Skip for now" writes the same flag file and leaves the
/// app in degraded mode (rules + cache only). Canceled or failed downloads keep their
/// <c>.partial</c> files, which the next run resumes.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject, IDisposable
{
    private const int MaxConcurrentDownloads = 2;

    private readonly ModelDownloader _downloader;
    private readonly ILogger<FirstRunViewModel> _log;
    private readonly string _flagPath;
    private CancellationTokenSource? _cts;
    private Task? _active;

    /// <summary>Creates the view model.</summary>
    /// <param name="downloader">HTTP downloader.</param>
    /// <param name="log">Logger. The UI passes NullLogger&lt;FirstRunViewModel&gt;.Instance.</param>
    /// <param name="baseDirectory">App root the relative manifest paths resolve against; defaults to <see cref="ModelManifest.DefaultBaseDirectory"/>.</param>
    /// <param name="flagPath">First-run flag file; defaults to <see cref="FirstRunGate.DefaultFlagPath"/>.</param>
    public FirstRunViewModel(
        ModelDownloader downloader,
        ILogger<FirstRunViewModel> log,
        string? baseDirectory = null,
        string? flagPath = null)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(log);
        _downloader = downloader;
        _log = log;
        BaseDirectory = baseDirectory ?? ModelManifest.DefaultBaseDirectory;
        _flagPath = flagPath ?? FirstRunGate.DefaultFlagPath;
        foreach (ModelFile file in ModelManifest.Required)
        {
            Files.Add(new FileViewModel(file));
        }
    }

    /// <summary>App root that relative manifest paths resolve against.</summary>
    public string BaseDirectory { get; }

    /// <summary>One row per required model file.</summary>
    public ObservableCollection<FileViewModel> Files { get; } = new();

    /// <summary>Overall progress across all required files, 0-100.</summary>
    [ObservableProperty]
    private double _overallProgress;

    /// <summary>Estimated time remaining, e.g. "about 2m 30s remaining"; "—" when idle.</summary>
    [ObservableProperty]
    private string _etaText = "\u2014";

    /// <summary>Footer status line.</summary>
    [ObservableProperty]
    private string _statusText = "Preparing\u2026";

    /// <summary>True while downloads are in flight.</summary>
    [ObservableProperty]
    private bool _isRunning;

    /// <summary>True when every required file is on disk; gates the Continue button.</summary>
    [ObservableProperty]
    private bool _allComplete;

    /// <summary>
    /// Raised (on the UI thread) when the wizard should close: all downloads finished,
    /// Continue, Skip, or Cancel.
    /// </summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Downloads every missing required model, at most two at a time. Files already on
    /// disk are marked complete without a request. A fully present set writes the flag
    /// file and raises <see cref="CloseRequested"/>.
    /// </summary>
    /// <returns>Completed work; per-file errors are shown in the row instead of thrown.</returns>
    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRunning || AllComplete)
        {
            return;
        }

        IsRunning = true;
        StatusText = "Downloading models\u2026";
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        var pending = new List<(ModelFile File, FileViewModel Row)>();
        foreach (FileViewModel row in Files)
        {
            string path = ModelManifest.ResolvePath(row.Model, BaseDirectory);
            if (File.Exists(path))
            {
                row.MarkComplete(new FileInfo(path).Length);
                continue;
            }

            row.SetStatus("Queued\u2026", "\u2026", "#888888");
            pending.Add((row.Model, row));
        }

        RefreshAllComplete();
        RecomputeOverall();
        if (AllComplete)
        {
            IsRunning = false;
            Finish();
            return;
        }

        using var throttle = new SemaphoreSlim(MaxConcurrentDownloads);
        List<Task> tasks = pending.Select(entry => DownloadOneAsync(entry.File, entry.Row, throttle, ct)).ToList();
        _active = Task.WhenAll(tasks);
        await _active; // Each task swallows its own errors; nothing below rethrows.

        IsRunning = false;
        RefreshAllComplete();
        RecomputeOverall();
        if (AllComplete)
        {
            Finish();
        }
        else if (Files.Any(f => f.StatusText == "Failed"))
        {
            StatusText = "Some downloads failed. Relaunch the app to retry, or skip for now.";
        }
        else
        {
            StatusText = "Canceled \u2014 partial downloads are kept and resume next time.";
        }
    }

    /// <summary>Downloads one file behind the shared throttle; never throws.</summary>
    /// <param name="model">Manifest entry (relative path).</param>
    /// <param name="row">Bound row to update.</param>
    /// <param name="throttle">Concurrency gate (2 slots).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Completed work.</returns>
    private async Task DownloadOneAsync(ModelFile model, FileViewModel row, SemaphoreSlim throttle, CancellationToken ct)
    {
        await throttle.WaitAsync(ct);
        try
        {
            row.SetStatus("Downloading\u2026", "\u2193", "#4A9EFF");
            ModelFile resolved = model with { TargetPath = ModelManifest.ResolvePath(model, BaseDirectory) };

            // No ConfigureAwait(false): row bindings are UI-bound, continuations must
            // return to the dispatcher thread.
            var progress = new Progress<DownloadProgress>(p => OnDownloadProgress(row, p));
            await _downloader.DownloadAsync(resolved, progress, ct);
            row.MarkComplete(row.Total > 0 ? row.Total : model.ExpectedSize);
        }
        catch (OperationCanceledException)
        {
            row.SetStatus("Canceled", "\u2016", "#888888");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            _log.LogWarning(ex, "Download failed for {Name}.", model.Name);
            row.SetStatus("Failed", "\u2717", "#F44336");
        }
        finally
        {
            throttle.Release();
            RefreshAllComplete();
            RecomputeOverall();
        }
    }

    /// <summary>Folds one progress report into the row and the overall totals.</summary>
    /// <param name="row">Row being downloaded.</param>
    /// <param name="p">Progress report.</param>
    private void OnDownloadProgress(FileViewModel row, DownloadProgress p)
    {
        row.Received = p.BytesReceived;
        row.Total = p.TotalBytes > 0 ? p.TotalBytes : row.Model.ExpectedSize;
        row.Speed = p.BytesPerSecond;

        double percent = row.Total > 0 ? Math.Clamp(p.BytesReceived * 100.0 / row.Total, 0, 100) : 0;
        string speed = p.BytesPerSecond > 0 ? $" \u00B7 {FileViewModel.FormatSize((long)p.BytesPerSecond)}/s" : string.Empty;
        row.SetProgress(percent, $"{FileViewModel.FormatSize(p.BytesReceived)} / {FileViewModel.FormatSize(row.Total)}{speed}");
        RecomputeOverall();
    }

    /// <summary>Recomputes <see cref="AllComplete"/> from the rows.</summary>
    private void RefreshAllComplete() => AllComplete = Files.Count > 0 && Files.All(f => f.IsComplete);

    /// <summary>Recomputes <see cref="OverallProgress"/> and <see cref="EtaText"/> from the rows.</summary>
    private void RecomputeOverall()
    {
        long received = 0;
        long total = 0;
        double speed = 0;
        foreach (FileViewModel file in Files)
        {
            long fileTotal = file.Total > 0 ? file.Total : file.Model.ExpectedSize;
            received += Math.Min(file.Received, fileTotal);
            total += fileTotal;
            speed += file.Speed;
        }

        OverallProgress = total > 0 ? Math.Clamp(received * 100.0 / total, 0, 100) : 0;
        if (!IsRunning)
        {
            EtaText = "\u2014";
            return;
        }

        long remaining = total - received;
        if (remaining <= 0)
        {
            EtaText = "finishing\u2026";
        }
        else if (speed > 0)
        {
            EtaText = $"about {FormatEta(remaining / speed)} remaining";
        }
        else
        {
            EtaText = "estimating\u2026";
        }
    }

    /// <summary>Formats a duration in seconds as e.g. "2m 30s" or "45s".</summary>
    /// <param name="seconds">Remaining seconds.</param>
    /// <returns>Human-readable duration.</returns>
    private static string FormatEta(double seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalMinutes >= 1
            ? $"{(int)time.TotalMinutes}m {time.Seconds:D2}s"
            : $"{Math.Max(1, (int)time.TotalSeconds)}s";
    }

    /// <summary>Writes the flag file (best effort) and asks the shell to close the wizard.</summary>
    private void Finish()
    {
        try
        {
            FirstRunGate.MarkComplete(_flagPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not write the first-run flag file at {Path}.", _flagPath);
        }

        StatusText = "All models ready.";
        CloseRequested?.Invoke();
    }

    /// <summary>Cancels any in-flight downloads without raising <see cref="CloseRequested"/> (window close path).</summary>
    public void CancelDownloads()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down; nothing to cancel.
        }
    }

    /// <summary>Cancel: stops downloads, keeps partial files, closes the wizard (no flag written).</summary>
    /// <returns>Completed work.</returns>
    [RelayCommand]
    private async Task CancelAsync()
    {
        CancelDownloads();
        await ObserveActiveAsync();

        // No ConfigureAwait(false): StatusText is UI-bound.
        IsRunning = false;
        RecomputeOverall();
        StatusText = "Canceled \u2014 partial downloads are kept and resume next time.";
        CloseRequested?.Invoke();
    }

    /// <summary>Skip for now: stops downloads, writes the flag file, closes into degraded mode.</summary>
    /// <returns>Completed work.</returns>
    [RelayCommand]
    private async Task SkipAsync()
    {
        CancelDownloads();
        await ObserveActiveAsync();

        // No ConfigureAwait(false): StatusText is UI-bound.
        IsRunning = false;
        Finish();
    }

    /// <summary>Continue: closes the wizard once every model is present.</summary>
    [RelayCommand]
    private void Continue()
    {
        if (AllComplete)
        {
            CloseRequested?.Invoke();
        }
    }

    /// <summary>Awaits the running downloads so nothing writes to disk after the wizard closes.</summary>
    /// <returns>Completed work.</returns>
    private async Task ObserveActiveAsync()
    {
        if (_active is null)
        {
            return;
        }

        try
        {
            await _active;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Download tasks swallow their own errors; this is a defensive net.
            _log.LogDebug(ex, "Download batch ended with an exception while closing.");
        }
    }

    /// <summary>Releases the cancellation token source owned by this view model.</summary>
    public void Dispose()
    {
        _cts?.Dispose();
        _cts = null;
        GC.SuppressFinalize(this);
    }
}