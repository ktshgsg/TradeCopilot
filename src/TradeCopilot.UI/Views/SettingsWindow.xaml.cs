using System.IO;
using System.Windows;
using System.Diagnostics;
using System.Globalization;
using TradeCopilot.UI.Logic;
using TradeCopilot.UI.Logic.Models;
using TradeCopilot.UI.Logic.Services;
using TradeCopilot.UI.Logic.ViewModels;

namespace TradeCopilot.UI.Views;

/// <summary>Tabbed settings dialog: Capture, Language, Voice, Privacy, About. Persists to settings.json.</summary>
public partial class SettingsWindow : Window
{
    private readonly UiSettings _settings;
    private readonly SettingsStore _store;
    private readonly IPipeClient _pipe;
    private readonly MainViewModel _viewModel;

    /// <summary>Creates the dialog with the loaded settings.</summary>
    /// <param name="settings">Shared settings instance (singleton from the container).</param>
    /// <param name="store">Persistence for settings.json.</param>
    /// <param name="pipe">Pipe transport for "purge_cache".</param>
    /// <param name="viewModel">Overlay view model; its MicAvailable flag follows the push-to-talk toggle.</param>
    public SettingsWindow(UiSettings settings, SettingsStore store, IPipeClient pipe, MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(pipe);
        ArgumentNullException.ThrowIfNull(viewModel);
        _settings = settings;
        _store = store;
        _pipe = pipe;
        _viewModel = viewModel;
        InitializeComponent();
        LoadFields();
    }

    private void LoadFields()
    {
        RegionXBox.Text = _settings.RegionX.ToString(CultureInfo.InvariantCulture);
        RegionYBox.Text = _settings.RegionY.ToString(CultureInfo.InvariantCulture);
        RegionWBox.Text = _settings.RegionWidth.ToString(CultureInfo.InvariantCulture);
        RegionHBox.Text = _settings.RegionHeight.ToString(CultureInfo.InvariantCulture);
        FpsSlider.Value = _settings.Fps;
        FpsLabel.Text = $"{_settings.Fps} FPS";

        LangAutoRadio.IsChecked = _settings.Language == "auto";
        LangEnRadio.IsChecked = _settings.Language == "en";
        LangHiRadio.IsChecked = _settings.Language == "hi";

        VoiceOutputCheck.IsChecked = _settings.VoiceOutputEnabled;
        PushToTalkCheck.IsChecked = _settings.PushToTalkEnabled;
        EncryptCacheCheck.IsChecked = _settings.EncryptCache;

        PatternList.Items.Clear();
        foreach (string pattern in _settings.ExclusionPatterns)
        {
            PatternList.Items.Add(pattern);
        }

        VersionText.Text = $"Version {typeof(SettingsWindow).Assembly.GetName().Version?.ToString(3) ?? "unknown"}";
    }

    private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FpsLabel is not null)
        {
            FpsLabel.Text = $"{(int)e.NewValue} FPS";
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.RegionX = ParseInt(RegionXBox.Text, _settings.RegionX);
        _settings.RegionY = ParseInt(RegionYBox.Text, _settings.RegionY);
        _settings.RegionWidth = ParseInt(RegionWBox.Text, _settings.RegionWidth);
        _settings.RegionHeight = ParseInt(RegionHBox.Text, _settings.RegionHeight);
        _settings.Fps = (int)FpsSlider.Value;
        _settings.Language = LangEnRadio.IsChecked == true ? "en" : LangHiRadio.IsChecked == true ? "hi" : "auto";
        _settings.VoiceOutputEnabled = VoiceOutputCheck.IsChecked == true;
        _settings.PushToTalkEnabled = PushToTalkCheck.IsChecked == true;
        _settings.ExclusionPatterns = PatternList.Items.Cast<string>().ToList();

        try
        {
            _store.Save(_settings);
            _viewModel.MicAvailable = _viewModel.HasRecorder && _settings.PushToTalkEnabled;
            StatusText.Text = $"Saved to {_store.FilePath}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private static int ParseInt(string text, int fallback) =>
        int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value >= 0
            ? value
            : fallback;

    private void AddPatternButton_Click(object sender, RoutedEventArgs e)
    {
        string pattern = PatternInput.Text.Trim();
        if (pattern.Length == 0 || PatternList.Items.Contains(pattern))
        {
            return;
        }

        PatternList.Items.Add(pattern);
        PatternInput.Clear();
    }

    private void RemovePatternButton_Click(object sender, RoutedEventArgs e)
    {
        if (PatternList.SelectedItem is string pattern)
        {
            PatternList.Items.Remove(pattern);
        }
    }

    private async void PurgeCacheButton_Click(object sender, RoutedEventArgs e)
    {
        PurgeStatusText.Text = "Purging...";
        PipeResponse resp = await _pipe.SendAsync(new PipeRequest("purge_cache"));
        PurgeStatusText.Text = resp.Type == "ok" ? "Cache purged." : $"Failed: {resp.Message ?? "service unreachable"}";
    }

    private void OpenAuditFolderButton_Click(object sender, RoutedEventArgs e)
    {
        // v1 note: the service writes data/audit.log next to its own executable; this opens
        // the same relative folder under the UI's base directory (shared install dir in v1.1).
        string dir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
