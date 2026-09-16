using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using InnovaEvidenceCapture.Models;
using WinForms = System.Windows.Forms;

// Este overlay es todo WPF. Los aliases dejan explicito cual de los dos mundos
// se usa, porque WinForms tiene tipos con exactamente los mismos nombres.
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// Overlay tipo "tijeras": oscurece el escritorio y deja arrastrar un rectangulo.
/// Al soltar aparece una barra para elegir foto o grabacion, igual que el recorte
/// de Windows 11: el agente no tiene que decidir antes de ver lo que selecciono.
/// </summary>
public sealed class SelectionOverlay : Window
{
    private readonly Canvas _canvas = new();
    private readonly System.Windows.Shapes.Rectangle _box = new();
    private readonly TextBlock _hint = new();
    private readonly Border _toolbar;
    private readonly TextBlock _sizeLabel = new();

    private System.Windows.Point _start;
    private bool _dragging;
    private readonly System.Drawing.Rectangle _virtualScreen = WinForms.SystemInformation.VirtualScreen;
    private readonly CaptureMode? _directMode;

    public System.Drawing.Rectangle? Region { get; private set; }
    public CaptureMode Mode { get; private set; } = CaptureMode.Photo;

    /// <param name="directMode">
    /// Si viene con valor (por ejemplo desde Ctrl+Shift+V) se salta la barra y
    /// devuelve ese modo apenas se suelta el mouse.
    /// </param>
    public SelectionOverlay(CaptureMode? directMode = null)
    {
        _directMode = directMode;

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

        _hint.Text = directMode == CaptureMode.Video
            ? "Arrastra para elegir la region a grabar  ·  Esc para cancelar"
            : "Arrastra para seleccionar la region  ·  Esc para cancelar";
        _hint.Foreground = System.Windows.Media.Brushes.White;
        _hint.FontSize = 16;
        _hint.Margin = new Thickness(24);
        _hint.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            ShadowDepth = 0, BlurRadius = 8, Color = Colors.Black, Opacity = 0.9
        };

        _sizeLabel.Foreground = System.Windows.Media.Brushes.White;
        _sizeLabel.FontSize = 12;
        _sizeLabel.FontWeight = FontWeights.SemiBold;
        _sizeLabel.Visibility = Visibility.Collapsed;
        _sizeLabel.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            ShadowDepth = 0, BlurRadius = 6, Color = Colors.Black, Opacity = 0.9
        };

        _toolbar = BuildToolbar();
        _toolbar.Visibility = Visibility.Collapsed;

        _canvas.Children.Add(_box);
        _canvas.Children.Add(_sizeLabel);
        _canvas.Children.Add(_hint);
        _canvas.Children.Add(_toolbar);
        Content = _canvas;

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => { Activate(); Focus(); Keyboard.Focus(this); };

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += OnKeyDown;
    }

    private DpiScale Dpi => VisualTreeHelper.GetDpi(this);

    private Border BuildToolbar()
    {
        var photo = ToolbarButton("📷  Foto", System.Windows.Media.Color.FromRgb(0x2F, 0x7D, 0xE1));
        photo.Click += (_, _) => Finish(CaptureMode.Photo);

        var video = ToolbarButton("⏺  Grabar", System.Windows.Media.Color.FromRgb(0xC0, 0x39, 0x39));
        video.Click += (_, _) => Finish(CaptureMode.Video);

        var cancel = ToolbarButton("Cancelar", System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44));
        cancel.Click += (_, _) => { Region = null; Close(); };

        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(photo);
        row.Children.Add(video);
        row.Children.Add(cancel);

        return new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1D, 0x21)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Child = row
        };
    }

    private static System.Windows.Controls.Button ToolbarButton(string text, System.Windows.Media.Color background) => new()
    {
        Content = text,
        Padding = new Thickness(16, 8, 16, 8),
        Margin = new Thickness(3, 0, 3, 0),
        Foreground = System.Windows.Media.Brushes.White,
        Background = new SolidColorBrush(background),
        BorderThickness = new Thickness(0),
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Cursor = Cursors.Hand
    };

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var dpi = Dpi;
        Left = _virtualScreen.Left / dpi.DpiScaleX;
        Top = _virtualScreen.Top / dpi.DpiScaleY;
        Width = _virtualScreen.Width / dpi.DpiScaleX;
        Height = _virtualScreen.Height / dpi.DpiScaleY;
    }

    // ------------------------------------------------------------- mouse

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        // Si la barra ya esta visible y el clic cae sobre ella, no se reinicia
        // la seleccion: el clic es para el boton.
        if (_toolbar.Visibility == Visibility.Visible && IsOverToolbar(e.GetPosition(_canvas))) return;

        _start = e.GetPosition(_canvas);
        _dragging = true;

        _hint.Visibility = Visibility.Collapsed;
        _toolbar.Visibility = Visibility.Collapsed;
        _box.Visibility = Visibility.Visible;
        _sizeLabel.Visibility = Visibility.Visible;

        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;

        var p = e.GetPosition(_canvas);
        double x = Math.Min(_start.X, p.X);
        double y = Math.Min(_start.Y, p.Y);
        double w = Math.Abs(p.X - _start.X);
        double h = Math.Abs(p.Y - _start.Y);

        Canvas.SetLeft(_box, x);
        Canvas.SetTop(_box, y);
        _box.Width = w;
        _box.Height = h;

        var dpi = Dpi;
        _sizeLabel.Text = $"{(int)Math.Round(w * dpi.DpiScaleX)} × {(int)Math.Round(h * dpi.DpiScaleY)} px";
        Canvas.SetLeft(_sizeLabel, x);
        Canvas.SetTop(_sizeLabel, Math.Max(0, y - 20));
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

        if (w < 16 || h < 16)
        {
            // Seleccion demasiado chica: se vuelve al estado inicial.
            Region = null;
            _box.Visibility = Visibility.Collapsed;
            _sizeLabel.Visibility = Visibility.Collapsed;
            _hint.Visibility = Visibility.Visible;
            return;
        }

        Region = new System.Drawing.Rectangle(x, y, w, h);

        if (_directMode is not null)
        {
            Finish(_directMode.Value);
            return;
        }

        ShowToolbar(Math.Min(_start.X, p.X), Math.Min(_start.Y, p.Y),
                    Math.Abs(p.X - _start.X), Math.Abs(p.Y - _start.Y));
    }

    private void ShowToolbar(double x, double y, double w, double h)
    {
        _toolbar.Visibility = Visibility.Visible;
        _toolbar.UpdateLayout();

        double toolbarWidth = _toolbar.ActualWidth > 0 ? _toolbar.ActualWidth : 330;
        double toolbarHeight = _toolbar.ActualHeight > 0 ? _toolbar.ActualHeight : 52;

        // Debajo de la seleccion; si no cabe, encima; si tampoco, dentro.
        double top = y + h + 10;
        if (top + toolbarHeight > ActualHeight) top = y - toolbarHeight - 10;
        if (top < 0) top = y + 10;

        double left = x + (w - toolbarWidth) / 2;
        left = Math.Max(8, Math.Min(left, ActualWidth - toolbarWidth - 8));

        Canvas.SetLeft(_toolbar, left);
        Canvas.SetTop(_toolbar, top);
    }

    private bool IsOverToolbar(System.Windows.Point point)
    {
        double left = Canvas.GetLeft(_toolbar);
        double top = Canvas.GetTop(_toolbar);
        return point.X >= left && point.X <= left + _toolbar.ActualWidth
            && point.Y >= top && point.Y <= top + _toolbar.ActualHeight;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Region = null;
            Close();
            return;
        }

        if (Region is null) return;

        // Atajos para no tener que soltar el teclado.
        if (e.Key == Key.Enter) Finish(CaptureMode.Photo);
        else if (e.Key == Key.V) Finish(CaptureMode.Video);
    }

    private void Finish(CaptureMode mode)
    {
        Mode = mode;
        Close();
    }
}
