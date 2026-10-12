using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using TradeCopilot.UI.Infrastructure;
using TradeCopilot.UI.Logic.ViewModels;

namespace TradeCopilot.UI;

/// <summary>Overlay window: chat bubbles, status bar, input bar, tray icon, and global hotkeys.</summary>
public partial class MainWindow : Window
{
    private static readonly BitmapImage TrayActiveImage = FreezeImage("pack://application:,,,/Assets/tray.ico");
    private static readonly BitmapImage TrayPausedImage = FreezeImage("pack://application:,,,/Assets/tray-paused.ico");

    private static BitmapImage FreezeImage(string uri)
    {
        var image = new BitmapImage(new Uri(uri, UriKind.Absolute));
        image.Freeze();
        return image;
    }

    private const int HotkeyIdToggleOverlay = 1;
    private const int HotkeyIdPushToTalk = 2;
    private const int HotkeyIdPauseResume = 3;
    private const int HotkeyIdPanic = 4;

    private const uint VkSpace = 0x20;
    private const uint VkKeyP = 0x50;
    private const uint VkKeyX = 0x58;

    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _statusTimer;
    private readonly Dictionary<int, GlobalHotkey> _hotkeys = new();
    private HwndSource? _hookSource;
    private bool _allowClose;

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
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.ShowOverlayRequested += ToggleOverlay;
        Loaded += MainWindow_Loaded;
        IsVisibleChanged += MainWindow_IsVisibleChanged;

        // The context menu lives in its own visual tree, so it does not inherit the
        // window's DataContext. Anchor it to the view model explicitly.
        TaskbarIcon trayIcon = TrayIcon;
        if (trayIcon.ContextMenu is not null)
        {
            trayIcon.ContextMenu.DataContext = _viewModel;
        }

        trayIcon.TrayLeftMouseUp += (_, _) => ToggleOverlay();
        UpdateTrayIcon();

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusTimer.Tick += StatusTimer_Tick;
    }

    /// <summary>The tray icon defined in MainWindow.xaml resources.</summary>
    private TaskbarIcon TrayIcon => (TaskbarIcon)Resources["TrayIcon"]!;

    /// <summary>Shows the overlay if hidden, hides it if visible; focuses the input box when shown.</summary>
    public void ToggleOverlay()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Show();
        Activate();
        FocusInput();
    }

    /// <summary>Marks the window closeable so <c>Application.Shutdown</c> can exit the process.</summary>
    public void AllowClose() => _allowClose = true;

    /// <summary>Focuses the chat input box (dispatched so it runs after Show/Activate).</summary>
    public void FocusInput() => Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => InputBox.Focus()));

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

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.CaptureActive))
        {
            UpdateTrayIcon();
        }
    }

    /// <summary>Swaps the tray icon: green when capture is live, red when paused.</summary>
    private void UpdateTrayIcon() => TrayIcon.IconSource =
        _viewModel.CaptureActive ? TrayActiveImage : TrayPausedImage;

    /// <summary>Registers the four global hotkeys once the window handle exists.</summary>
    /// <param name="e">Event args.</param>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        nint hwnd = new WindowInteropHelper(this).Handle;
        _hookSource = HwndSource.FromHwnd(hwnd);
        _hookSource?.AddHook(WndProcHook);

        RegisterHotkey(hwnd, HotkeyIdToggleOverlay, GlobalHotkey.ModControl, VkSpace, ToggleOverlay);
        RegisterHotkey(
            hwnd,
            HotkeyIdPushToTalk,
            GlobalHotkey.ModControl | GlobalHotkey.ModAlt | GlobalHotkey.ModNoRepeat,
            VkSpace,
            () => _viewModel.ToggleRecordingCommand.Execute(null));
        RegisterHotkey(
            hwnd,
            HotkeyIdPauseResume,
            GlobalHotkey.ModControl | GlobalHotkey.ModShift | GlobalHotkey.ModNoRepeat,
            VkKeyP,
            TogglePauseResume);
        RegisterHotkey(
            hwnd,
            HotkeyIdPanic,
            GlobalHotkey.ModControl | GlobalHotkey.ModShift | GlobalHotkey.ModNoRepeat,
            VkKeyX,
            () => _viewModel.PanicCommand.Execute(null));
    }

    /// <summary>
    /// WPF's WndProc equivalent (System.Windows.Window has no WndProc override): intercepts
    /// WM_HOTKEY and fires the matching hotkey's Pressed event.
    /// </summary>
    private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GlobalHotkey.WmHotkey && wParam != IntPtr.Zero &&
            _hotkeys.TryGetValue(wParam.ToInt32(), out GlobalHotkey? hotkey))
        {
            hotkey.RaisePressed();
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>Registers one hotkey and wires its action; unregistered combos are left inert.</summary>
    private void RegisterHotkey(nint hwnd, int id, uint modifiers, uint key, Action onPress)
    {
        var hotkey = new GlobalHotkey(hwnd, id, modifiers, key);
        _hotkeys[id] = hotkey;
        if (hotkey.Registered)
        {
            hotkey.Pressed += onPress;
        }
    }

    private void TogglePauseResume()
    {
        if (_viewModel.CaptureActive)
        {
            _viewModel.PauseCaptureCommand.Execute(null);
        }
        else
        {
            _viewModel.ResumeCaptureCommand.Execute(null);
        }
    }

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
    protected override void OnClosing(CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    /// <summary>Releases hotkeys and the tray icon when the window finally closes (process exit).</summary>
    /// <param name="e">Event args.</param>
    protected override void OnClosed(EventArgs e)
    {
        foreach (GlobalHotkey hotkey in _hotkeys.Values)
        {
            hotkey.Dispose();
        }

        _hotkeys.Clear();
        _hookSource?.RemoveHook(WndProcHook);
        _hookSource = null;
        if (Resources["TrayIcon"] is IDisposable trayIcon)
        {
            trayIcon.Dispose();
        }

        base.OnClosed(e);
    }
}
