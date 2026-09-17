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

        uint vk = ParseKey(key);
        if (vk == 0) return false;

        int id = _nextId++;
        if (!RegisterHotKey(_handle, id, mods, vk))
            return false;

        _handlers[id] = handler;
        return true;
    }

    /// <summary>
    /// Acepta una letra o numero ("I", "5") y tambien teclas con nombre
    /// ("PrintScreen", "F9", "Pause"...). Las teclas con nombre permiten un
    /// atajo de UNA sola tecla, sin modificadores: en una sala de monitoreo, a
    /// oscuras y con una persecucion en pantalla, buscar Ctrl+Shift+I con la
    /// vista puesta en las camaras cuesta segundos que no siempre hay.
    /// </summary>
    private static uint ParseKey(string key)
    {
        var name = (key ?? "").Trim();
        if (name.Length == 0) return 0;

        switch (name.ToUpperInvariant())
        {
            case "PRINTSCREEN" or "PRTSC" or "IMPRPANT": return 0x2C; // VK_SNAPSHOT
            case "PAUSE" or "BREAK": return 0x13;
            case "SCROLLLOCK": return 0x91;
            case "INSERT" or "INS": return 0x2D;
            case "HOME": return 0x24;
            case "END": return 0x23;
            case "PAGEUP": return 0x21;
            case "PAGEDOWN": return 0x22;
        }

        // F1 a F12
        if ((name[0] == 'F' || name[0] == 'f') && name.Length is 2 or 3
            && int.TryParse(name.AsSpan(1), out int number)
            && number is >= 1 and <= 12)
        {
            return (uint)(0x70 + number - 1);
        }

        if (name.Length == 1) return char.ToUpperInvariant(name[0]);

        return 0;
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
