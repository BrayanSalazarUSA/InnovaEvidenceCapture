using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// Barra flotante durante la grabacion: punto rojo parpadeando, cronometro y
/// boton de detener. No roba el foco, para que el agente pueda seguir moviendose
/// en iVMS mientras graba.
/// </summary>
public sealed class RecordingBar : Window
{
    private readonly TextBlock _time = new();
    private readonly int _maxSeconds;

    public event Action? StopRequested;

    public RecordingBar(int maxSeconds)
    {
        _maxSeconds = maxSeconds;

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

        Content = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1D, 0x21)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 9, 9, 9),
            Child = row
        };

        Loaded += (_, _) => PlaceTopCenter();
    }

    private void PlaceTopCenter()
    {
        var work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - ActualWidth) / 2;
        Top = work.Top + 18;
    }

    public void UpdateElapsed(TimeSpan elapsed)
    {
        int seconds = Math.Min((int)elapsed.TotalSeconds, _maxSeconds);
        _time.Text = $"Grabando  {Format(seconds)} / {Format(_maxSeconds)}";
    }

    private static string Format(int totalSeconds) =>
        $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
}
