using System.ComponentModel;
using System.Windows;
using TradeCopilot.Core.Setup;
using TradeCopilot.UI.Logic.ViewModels;

namespace TradeCopilot.UI.Views;

/// <summary>
/// First-run model downloader wizard. Shown modally by <see cref="App"/> before the overlay
/// when the first-run flag file is missing and models are not yet on disk. Downloads start
/// as soon as the window loads; closing the window (buttons or X) cancels them and keeps
/// the <c>.partial</c> files so the next launch resumes.
/// </summary>
public partial class FirstRunWindow : Window
{
    private readonly FirstRunViewModel _viewModel;
    private bool _exitRequested;

    /// <summary>Creates the wizard.</summary>
    /// <param name="viewModel">Bound downloader view model.</param>
    public FirstRunWindow(FirstRunViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();
        _viewModel.CloseRequested += ViewModel_CloseRequested;
        Loaded += FirstRunWindow_Loaded;
        Closing += FirstRunWindow_Closing;
    }

    /// <summary>Kicks off the downloads the first time the window appears.</summary>
    /// <param name="sender">Window.</param>
    /// <param name="e">Routed event.</param>
    private void FirstRunWindow_Loaded(object sender, RoutedEventArgs e) =>
        _ = _viewModel.StartCommand.ExecuteAsync(null);

    /// <summary>Cancels downloads when the user closes with the X button (partial files stay).</summary>
    /// <param name="sender">Window.</param>
    /// <param name="e">Cancel event.</param>
    private void FirstRunWindow_Closing(object? sender, CancelEventArgs e)
    {
        _viewModel.CancelDownloads();
        // Hard block: the X button cannot dismiss the wizard unless every model
        // is already present. Only the explicit "Exit" button may close with
        // models missing (the caller then shuts the app down).
        if (!_exitRequested && !ModelManifest.AllPresent(_viewModel.BaseDirectory))
        {
            e.Cancel = true;
        }
    }

    /// <summary>Marks an explicit user exit so the Closing handler allows the window to close.</summary>
    private void ExitButton_Click(object sender, RoutedEventArgs e) => _exitRequested = true;

    /// <summary>Closes the modal when the view model signals completion, skip, or cancel.</summary>
    private void ViewModel_CloseRequested() => Close();
}