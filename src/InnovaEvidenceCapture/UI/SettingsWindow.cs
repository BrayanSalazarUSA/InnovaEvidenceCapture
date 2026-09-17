using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InnovaEvidenceCapture.Services;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// Ajustes de la estacion, desde la propia app.
///
/// Antes esto se cambiaba editando appsettings.json a mano en cada PC. Con 30
/// maquinas eso significa entrar por escritorio remoto una por una y, peor,
/// que ese archivo vive en Program Files y el agente no tiene permiso para
/// escribirlo. Lo que se cambia aqui se guarda en ProgramData.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly AppConfig _cfg;

    private readonly TextBox _stationCode = new();
    private readonly TextBox _stationName = new();
    private readonly HotkeyBox _photo;
    private readonly HotkeyBox _video;
    private readonly TextBlock _status = new();

    /// <summary>Se aplico algun cambio que obliga a re-registrar los atajos.</summary>
    public bool Saved { get; private set; }

    public SettingsWindow(AppConfig cfg)
    {
        _cfg = cfg;

        Title = "Innova Evidence Capture — ajustes";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1D, 0x21));

        _photo = new HotkeyBox(cfg.HotkeyModifiers, cfg.HotkeyKey);
        _video = new HotkeyBox(cfg.HotkeyModifiers, cfg.HotkeyVideoKey);

        _stationCode.Text = cfg.StationCode;
        _stationName.Text = cfg.StationName;

        var stack = new StackPanel { Margin = new Thickness(22) };

        stack.Children.Add(Section("Estacion"));
        stack.Children.Add(Field("Codigo (el que ve el agente en la app movil)", StyleBox(_stationCode)));
        stack.Children.Add(Field("Nombre largo", StyleBox(_stationName)));

        stack.Children.Add(Section("Atajos"));
        stack.Children.Add(new TextBlock
        {
            Text = "Haz clic en el recuadro y presiona la tecla que quieras usar. " +
                   "Una sola tecla es mas rapida que una combinacion: en una emergencia " +
                   "el agente no despega la vista de las camaras.",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x8A, 0x90, 0x99)),
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        stack.Children.Add(Field("Capturar", _photo));
        stack.Children.Add(Field("Grabar video", _video));

        _status.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x9A, 0x5A));
        _status.FontSize = 12;
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Margin = new Thickness(0, 6, 0, 0);
        stack.Children.Add(_status);

        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };

        var cancel = MakeButton("Cancelar", false);
        cancel.Click += (_, _) => Close();
        buttons.Children.Add(cancel);

        var save = MakeButton("Guardar", true);
        save.Click += (_, _) => Apply();
        buttons.Children.Add(save);

        stack.Children.Add(buttons);
        Content = stack;
    }

    private void Apply()
    {
        if (string.IsNullOrWhiteSpace(_stationCode.Text))
        {
            _status.Text = "El codigo de la estacion no puede quedar vacio.";
            return;
        }

        if (_photo.Key.Length == 0 || _video.Key.Length == 0)
        {
            _status.Text = "Falta elegir alguna de las dos teclas.";
            return;
        }

        if (_photo.Key.Equals(_video.Key, StringComparison.OrdinalIgnoreCase)
            && _photo.Modifiers.Equals(_video.Modifiers, StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "Capturar y grabar no pueden tener el mismo atajo.";
            return;
        }

        _cfg.StationCode = _stationCode.Text.Trim();
        _cfg.StationName = string.IsNullOrWhiteSpace(_stationName.Text)
            ? _cfg.StationCode
            : _stationName.Text.Trim();

        // Los dos atajos comparten modificadores: el de la foto manda.
        _cfg.HotkeyModifiers = _photo.Modifiers;
        _cfg.HotkeyKey = _photo.Key;
        _cfg.HotkeyVideoKey = _video.Key;

        var error = _cfg.Save();
        if (error is not null)
        {
            _status.Text = $"No se pudo guardar: {error}";
            return;
        }

        Saved = true;
        Close();
    }

    // ------------------------------------------------------------- piezas

    private static TextBlock Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0xA1, 0x3B)),
        FontSize = 11,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(0, 14, 0, 8)
    };

    private static UIElement Field(string label, UIElement input)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x9F, 0xA8)),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        });

        panel.Children.Add(input);
        return panel;
    }

    private static TextBox StyleBox(TextBox box)
    {
        box.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x28, 0x2D));
        box.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xEA, 0xED));
        box.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44));
        box.BorderThickness = new Thickness(1);
        box.Padding = new Thickness(8, 6, 8, 6);
        box.FontSize = 13;
        return box;
    }

    private static System.Windows.Controls.Button MakeButton(string text, bool primary)
    {
        return new System.Windows.Controls.Button
        {
            Content = text,
            Padding = new Thickness(18, 8, 18, 8),
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 13,
            Foreground = System.Windows.Media.Brushes.White,
            BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(primary
                ? System.Windows.Media.Color.FromRgb(0xC9, 0xA1, 0x3B)
                : System.Windows.Media.Color.FromRgb(0x2B, 0x2F, 0x36)),
            Cursor = Cursors.Hand
        };
    }

    /// <summary>
    /// Recuadro que aprende el atajo de la tecla que se presione, en vez de
    /// pedir que alguien escriba "PrintScreen" sin equivocarse.
    /// </summary>
    private sealed class HotkeyBox : Border
    {
        private readonly TextBlock _label = new();

        public string Modifiers { get; private set; }
        public string Key { get; private set; }

        public HotkeyBox(string modifiers, string key)
        {
            Modifiers = modifiers ?? "";
            Key = key ?? "";

            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x28, 0x2D));
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44));
            BorderThickness = new Thickness(1);
            Padding = new Thickness(10, 8, 10, 8);
            Focusable = true;
            Cursor = Cursors.Hand;

            _label.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xEA, 0xED));
            _label.FontSize = 13;
            Child = _label;
            Refresh();

            MouseLeftButtonDown += (_, _) => Keyboard.Focus(this);
            GotKeyboardFocus += (_, _) =>
            {
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC9, 0xA1, 0x3B));
                _label.Text = "Presiona la tecla...";
            };
            LostKeyboardFocus += (_, _) =>
            {
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44));
                Refresh();
            };

            PreviewKeyDown += OnKey;
        }

        private void OnKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            e.Handled = true;

            var pressed = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;

            // Los modificadores solos no son un atajo, se espera a la tecla real.
            if (pressed is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl
                or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift
                or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
                or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin)
            {
                return;
            }

            var name = NameOf(pressed);
            if (name is null) return;

            var mods = new List<string>();
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods.Add("Ctrl");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) mods.Add("Shift");
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) mods.Add("Alt");

            Modifiers = string.Join("+", mods);
            Key = name;
            Refresh();
        }

        /// <summary>Solo teclas que el registro global de Windows admite.</summary>
        private static string? NameOf(System.Windows.Input.Key key)
        {
            if (key is >= System.Windows.Input.Key.A and <= System.Windows.Input.Key.Z)
                return key.ToString();

            if (key is >= System.Windows.Input.Key.D0 and <= System.Windows.Input.Key.D9)
                return key.ToString().Substring(1);

            if (key is >= System.Windows.Input.Key.F1 and <= System.Windows.Input.Key.F12)
                return key.ToString();

            return key switch
            {
                System.Windows.Input.Key.Snapshot => "PrintScreen",
                System.Windows.Input.Key.Pause => "Pause",
                System.Windows.Input.Key.Scroll => "ScrollLock",
                System.Windows.Input.Key.Insert => "Insert",
                System.Windows.Input.Key.Home => "Home",
                System.Windows.Input.Key.End => "End",
                System.Windows.Input.Key.PageUp => "PageUp",
                System.Windows.Input.Key.PageDown => "PageDown",
                _ => null
            };
        }

        private void Refresh()
        {
            _label.Text = string.IsNullOrEmpty(Modifiers) ? Key : $"{Modifiers} + {Key}";
        }
    }
}
