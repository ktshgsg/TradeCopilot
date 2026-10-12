using System.Runtime.InteropServices;

namespace TradeCopilot.UI.Infrastructure;

/// <summary>
/// A system-wide hotkey registered against a window handle with <c>RegisterHotKey</c>.
/// The owner window's message hook (<c>HwndSource.AddHook</c>; WPF has no <c>WndProc</c>
/// override) matches <see cref="WmHotkey"/> messages on
/// <see cref="Id"/> and calls <see cref="RaisePressed"/>, which fires <see cref="Pressed"/>.
/// </summary>
public sealed partial class GlobalHotkey : IDisposable
{
    /// <summary>WM_HOTKEY: posted to the window that registered the combination.</summary>
    public const int WmHotkey = 0x0312;

    /// <summary>MOD_ALT modifier flag.</summary>
    public const uint ModAlt = 0x0001;

    /// <summary>MOD_CONTROL modifier flag.</summary>
    public const uint ModControl = 0x0002;

    /// <summary>MOD_SHIFT modifier flag.</summary>
    public const uint ModShift = 0x0004;

    /// <summary>MOD_NOREPEAT: suppress auto-repeat floods while the keys are held.</summary>
    public const uint ModNoRepeat = 0x4000;

    private readonly nint _hwnd;
    private readonly int _id;
    private bool _disposed;

    /// <summary>Creates the hotkey and registers it immediately.</summary>
    /// <param name="hwnd">Target window handle (valid after source initialization).</param>
    /// <param name="id">Application-defined id (1–0xBFFF), unique per window.</param>
    /// <param name="modifiers">Bitwise OR of the MOD_* constants.</param>
    /// <param name="key">Virtual-key code (e.g. 0x20 for Space).</param>
    public GlobalHotkey(nint hwnd, int id, uint modifiers, uint key)
    {
        _hwnd = hwnd;
        _id = id;
        Registered = RegisterHotKey(hwnd, id, modifiers, key);
    }

    /// <summary>True when Windows accepted the registration (false when another app owns the combo).</summary>
    public bool Registered { get; }

    /// <summary>Hotkey id passed to the constructor; matched against WM_HOTKEY's <c>WPARAM</c>.</summary>
    public int Id => _id;

    /// <summary>Raised on the window thread when the combination is pressed.</summary>
    public event Action? Pressed;

    /// <summary>Invokes <see cref="Pressed"/>. Called by the owner window's message hook on WM_HOTKEY.</summary>
    public void RaisePressed() => Pressed?.Invoke();

    /// <summary>Unregisters the hotkey. Idempotent.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Registered)
        {
            UnregisterHotKey(_hwnd, _id);
        }

        Pressed = null;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnregisterHotKey(nint hWnd, int id);
}
