using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinForms = System.Windows.Forms;

// WinForms entra por los implicit usings (lo usamos solo para el icono de la
// bandeja) y varios de sus tipos se llaman igual que los de WPF. Este overlay es
// todo WPF, asi que se fijan los aliases una vez.
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// Overlay tipo "tijeras": oscurece el escritorio completo y deja arrastrar un
/// rectangulo. Devuelve la region en pixeles fisicos.
/// </summary>
public sealed class SelectionOverlay : Window
{
    private readonly Canvas _canvas = new();
    private readonly System.Windows.Shapes.Rectangle _box = new();
    private readonly TextBlock _hint = new();

    private System.Windows.Point _start;
    private bool _dragging;
    private readonly System.Drawing.Rectangle _virtualScreen = WinForms.SystemInformation.VirtualScreen;

    public System.Drawing.Rectangle? Result { get; private set; }

    public SelectionOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Cursor = Cursors.Cross;

        _box.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3B, 0x9E, 0xF5));
        _box.StrokeThickness = 2;
        _box.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(48, 0x3B, 0x9E, 0xF5));
        _box.Visibility = Visibility.Collapsed;

        _hint.Text = "Arrastra para seleccionar la region  ·  Esc para cancelar";
        _hint.Foreground = System.Windows.Media.Brushes.White;
        _hint.FontSize = 16;
        _hint.Margin = new Thickness(24);
        _hint.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            ShadowDepth = 0, BlurRadius = 8, Color = Colors.Black, Opacity = 0.9
        };

        _canvas.Children.Add(_box);
        _canvas.Children.Add(_hint);
        Content = _canvas;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { Activate(); Focus(); Keyboard.Focus(this); };

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { Result = null; Close(); } };
    }

    private DpiScale Dpi => VisualTreeHelper.GetDpi(this);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var dpi = Dpi;
        Left = _virtualScreen.Left / dpi.DpiScaleX;
        Top = _virtualScreen.Top / dpi.DpiScaleY;
        Width = _virtualScreen.Width / dpi.DpiScaleX;
        Height = _virtualScreen.Height / dpi.DpiScaleY;
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(_canvas);
        _dragging = true;
        _hint.Visibility = Visibility.Collapsed;
        _box.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        var p = e.GetPosition(_canvas);
        double x = Math.Min(_start.X, p.X);
        double y = Math.Min(_start.Y, p.Y);

        Canvas.SetLeft(_box, x);
        Canvas.SetTop(_box, y);
        _box.Width = Math.Abs(p.X - _start.X);
        _box.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        var p = e.GetPosition(_canvas);
        var dpi = Dpi;

        int x = _virtualScreen.Left + (int)Math.Round(Math.Min(_start.X, p.X) * dpi.DpiScaleX);
        int y = _virtualScreen.Top + (int)Math.Round(Math.Min(_start.Y, p.Y) * dpi.DpiScaleY);
        int w = (int)Math.Round(Math.Abs(p.X - _start.X) * dpi.DpiScaleX);
        int h = (int)Math.Round(Math.Abs(p.Y - _start.Y) * dpi.DpiScaleY);

        Result = (w >= 8 && h >= 8) ? new System.Drawing.Rectangle(x, y, w, h) : null;
        Close();
    }
}
