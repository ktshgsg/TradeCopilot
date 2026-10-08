using System.Collections.Specialized;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TradeCopilot.UI.Logic.ViewModels;

namespace TradeCopilot.UI;

/// <summary>Overlay window: chat bubbles, status bar, input bar, and the 5s status poll.</summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _statusTimer;

    /// <summary>Creates the overlay bound to the given view model.</summary>
    /// <param name="viewModel">Conversation state and pipe commands.</param>
    public MainWindow(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        DataContext = _viewModel;
        InitializeComponent();

        var area = SystemParameters.WorkArea;
        Left = area.Right - Width - 16;
        Top = area.Top + 16;

        _viewModel.Messages.CollectionChanged += Messages_CollectionChanged;
        Loaded += MainWindow_Loaded;
        IsVisibleChanged += MainWindow_IsVisibleChanged;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += StatusTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InputBox.Focus();
        await _viewModel.RefreshStatusAsync().ConfigureAwait(true);
        _statusTimer.Start();
    }

    private void MainWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_statusTimer is null)
        {
            return;
        }

        if (IsVisible)
        {
            _statusTimer.Start();
        }
        else
        {
            _statusTimer.Stop();
        }
    }

    private async void StatusTimer_Tick(object? sender, EventArgs e)
    {
        if (!IsVisible)
        {
            return;
        }

        await _viewModel.RefreshStatusAsync().ConfigureAwait(true);
    }

    private void Messages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        MessageScroll.ScrollToBottom();

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

    /// <summary>Hides on Esc instead of closing.</summary>
    /// <param name="e">Key event.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>Cancels close and hides, so the tray Quit command owns shutdown.</summary>
    /// <param name="e">Cancel event.</param>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
