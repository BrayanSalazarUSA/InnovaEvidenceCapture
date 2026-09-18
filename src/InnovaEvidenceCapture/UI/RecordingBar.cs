using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using InnovaEvidenceCapture.Services;
using WinForms = System.Windows.Forms;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// Barra flotante durante la grabacion: punto rojo parpadeando, cronometro y
/// boton de detener. No roba el foco, para que el agente pueda seguir moviendose
/// en iVMS mientras graba.
///
/// Dos cosas la mantienen fuera del video:
/// 1) CaptureShield: Windows deja de mostrarla a quien capture la pantalla.
/// 2) Posicion: igual se coloca fuera del rectangulo que se esta grabando,
///    por si (1) no esta disponible en ese PC.
/// </summary>
public sealed class RecordingBar : Window
{
    private readonly TextBlock _time = new();
    private readonly int _maxSeconds;
    private readonly System.Drawing.Rectangle _region;

    /// <summary>true si Windows la esta escondiendo de las capturas.</summary>
    public bool HiddenFromCapture { get; private set; }

    public event Action? StopRequested;

    /// <param name="region">Rectangulo que se esta grabando, en pixeles.</param>
    /// <param name="stopHint">Atajo que tambien detiene, para mostrarlo en la barra.</param>
    public RecordingBar(int maxSeconds, System.Drawing.Rectangle region, string? stopHint = null)
    {
        _maxSeconds = maxSeconds;
        _region = region;

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0x3B, 0x3B)),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        var blink = new DoubleAnimation(1.0, 0.25, new Duration(TimeSpan.FromSeconds(0.7)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        dot.BeginAnimation(OpacityProperty, blink);

        _time.Text = $"Grabando  00:00 / {Format(maxSeconds)}";
        _time.Foreground = System.Windows.Media.Brushes.White;
        _time.FontSize = 14;
        _time.FontWeight = FontWeights.SemiBold;
        _time.Margin = new Thickness(10, 0, 14, 0);
        _time.VerticalAlignment = System.Windows.VerticalAlignment.Center;

        var stop = new System.Windows.Controls.Button
        {
            Content = "Detener y guardar",
            Padding = new Thickness(14, 7, 14, 7),
            Foreground = System.Windows.Media.Brushes.White,
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2F, 0x7D, 0xE1)),
            BorderThickness = new Thickness(0),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        stop.Click += (_, _) => StopRequested?.Invoke();

        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        row.Children.Add(dot);
        row.Children.Add(_time);
        row.Children.Add(stop);

        if (!string.IsNullOrWhiteSpace(stopHint))
        {
            row.Children.Add(new TextBlock
            {
                Text = $"o {stopHint}",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0xA1, 0xAD)),
                FontSize = 12,
                Margin = new Thickness(10, 0, 4, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
        }

        Content = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1D, 0x21)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 9, 9, 9),
            Child = row
        };

        SourceInitialized += (_, _) => HiddenFromCapture = CaptureShield.Hide(this);
        Loaded += (_, _) => Place();
    }

    /// <summary>
    /// La deja fuera del area grabada si hay espacio: debajo, arriba o al lado.
    /// Si la grabacion es de toda la pantalla no queda afuera nada, y ahi la
    /// unica defensa es CaptureShield.
    /// </summary>
    private void Place()
    {
        double w = ActualWidth;
        double h = ActualHeight;
        const double margin = 12;

        var screen = WinForms.SystemInformation.VirtualScreen;
        double minLeft = screen.Left + margin;
        double maxLeft = screen.Right - w - margin;

        double centered = Clamp(_region.Left + (_region.Width - w) / 2, minLeft, maxLeft);

        if (_region.Bottom + margin + h <= screen.Bottom)
        {
            Left = centered;
            Top = _region.Bottom + margin;
            return;
        }

        if (_region.Top - margin - h >= screen.Top)
        {
            Left = centered;
            Top = _region.Top - margin - h;
            return;
        }

        if (_region.Right + margin + w <= screen.Right)
        {
            Left = _region.Right + margin;
            Top = Clamp(_region.Top + margin, screen.Top + margin, screen.Bottom - h - margin);
            return;
        }

        if (_region.Left - margin - w >= screen.Left)
        {
            Left = _region.Left - margin - w;
            Top = Clamp(_region.Top + margin, screen.Top + margin, screen.Bottom - h - margin);
            return;
        }

        // Se esta grabando todo: encima, pero arriba y al centro del area.
        Left = centered;
        Top = _region.Top + margin;

        if (!HiddenFromCapture)
        {
            LogService.Warn(
                "La grabacion cubre toda la pantalla y este Windows no puede ocultar la barra " +
                "de la captura: va a salir dentro del video. Seleccionar una region mas chica lo evita.");
        }
    }

    public void UpdateElapsed(TimeSpan elapsed)
    {
        int seconds = Math.Min((int)elapsed.TotalSeconds, _maxSeconds);
        _time.Text = $"Grabando  {Format(seconds)} / {Format(_maxSeconds)}";
    }

    private static double Clamp(double value, double min, double max) =>
        max < min ? min : Math.Min(Math.Max(value, min), max);

    private static string Format(int totalSeconds) =>
        $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
}
