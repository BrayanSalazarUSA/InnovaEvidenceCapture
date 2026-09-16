using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace InnovaEvidenceCapture.Services;

/// <summary>
/// Registra un hotkey global de Windows (funciona aunque iVMS tenga el foco).
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;
    private const uint MOD_NOREPEAT = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private readonly Window _sink;
    private readonly IntPtr _handle;
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 9000;
    private bool _disposed;

    public HotkeyService()
    {
        _sink = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = false,
            Visibility = Visibility.Hidden
        };

        _handle = new WindowInteropHelper(_sink).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle)!;
        _source.AddHook(WndProc);
    }

    /// <param name="modifiers">Ej. "Ctrl+Shift"</param>
    /// <param name="key">Ej. "I"</param>
    public bool Register(string modifiers, string key, Action handler)
    {
        uint mods = MOD_NOREPEAT;
        foreach (var part in modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.Trim().ToUpperInvariant())
            {
                case "CTRL" or "CONTROL": mods |= MOD_CONTROL; break;
                case "SHIFT": mods |= MOD_SHIFT; break;
                case "ALT": mods |= MOD_ALT; break;
                case "WIN": mods |= MOD_WIN; break;
            }
        }

        if (string.IsNullOrWhiteSpace(key)) return false;
        uint vk = char.ToUpperInvariant(key.Trim()[0]);

        int id = _nextId++;
        if (!RegisterHotKey(_handle, id, mods, vk))
            return false;

        _handlers[id] = handler;
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            action();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var id in _handlers.Keys)
            UnregisterHotKey(_handle, id);

        _handlers.Clear();
        _source.RemoveHook(WndProc);
        _sink.Close();
    }
}
