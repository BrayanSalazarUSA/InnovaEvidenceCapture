using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// Aviso propio en la esquina inferior derecha.
///
/// Antes esto eran globos del area de notificaciones de Windows: ocupaban media
/// esquina, se quedaban apilados en el centro de notificaciones y salia uno por
/// cada archivo que se subia. El agente esta mirando camaras, no puede estar
/// cerrando avisos.
///
/// Ahora es una sola linea baja que se va sola: 2,5 s cuando todo salio bien y
/// 7 s cuando fue un error (ese si hay que leerlo). Se puede cerrar con un clic.
/// </summary>
public sealed class Toast : Window
{
    private static readonly List<Toast> Open = new();

    private const double ToastWidth = 330;
    private const double ToastHeight = 38;
    private const double Gap = 8;
    private const double ScreenMargin = 14;

    private readonly DispatcherTimer _timer;
    private bool _closing;

    private Toast(string text, bool isError)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;   // no le quita el foco al cliente de camaras
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Manual;
        Width = ToastWidth;
        Height = ToastHeight;
        Opacity = 0;

        var accent = isError
            ? System.Windows.Media.Color.FromRgb(0xE6, 0x3C, 0x32)
            : System.Windows.Media.Color.FromRgb(0x3F, 0xB9, 0x50);

        var body = new Border
        {
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x20, 0x23, 0x28)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x35, 0x39, 0x40)),
            BorderThickness = new Thickness(1)
        };

        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        layout.ColumnDefinitions.Add(new ColumnDefinition());

        var stripe = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new CornerRadius(7, 0, 0, 7)
        };
        Grid.SetColumn(stripe, 0);
        layout.Children.Add(stripe);

        var label = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0xEA, 0xED)),
            FontSize = 12.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(11, 0, 11, 0)
        };
        Grid.SetColumn(label, 1);
        layout.Children.Add(label);

        body.Child = layout;
        Content = body;

        ToolTip = text;
        MouseLeftButtonDown += (_, _) => FadeOut();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(isError ? 7 : 2.5) };
        _timer.Tick += (_, _) => FadeOut();
    }

    /// <summary>
    /// Muestra un aviso. Se puede llamar desde cualquier hilo.
    ///
    /// No se llama Show a proposito: ese nombre taparia el Show() que hereda de
    /// Window y ni siquiera compilaria.
    /// </summary>
    public static void Notify(string text, bool isError = false)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return;

        try
        {
            app.Dispatcher.InvokeAsync(ShowCore);
        }
        catch
        {
            // La app se esta cerrando: el aviso ya no le sirve a nadie.
        }

        void ShowCore()
        {
            try
            {
                // Mas de tres avisos a la vez ya es la misma molestia de antes.
                while (Open.Count >= 3) Open[0].FadeOut();

                var toast = new Toast(text, isError);
                Open.Add(toast);
                toast.Show();
                Reposition();
                toast.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(140)));
                toast._timer.Start();
            }
            catch
            {
                // Un aviso nunca puede tumbar la captura.
            }
        }
    }

    private void FadeOut()
    {
        if (_closing) return;
        _closing = true;
        _timer.Stop();

        // Sale de la lista ya, no cuando termine la animacion: si no, el
        // recorte a tres avisos se quedaria dando vueltas esperando que baje.
        Open.Remove(this);

        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(180));
        fade.Completed += (_, _) =>
        {
            Close();
            Reposition();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>
    /// Los apila hacia arriba desde la esquina, respetando la barra de tareas.
    /// </summary>
    private static void Reposition()
    {
        var area = SystemParameters.WorkArea;
        double bottom = area.Bottom - ScreenMargin;

        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var toast = Open[i];
            toast.Left = area.Right - ToastWidth - ScreenMargin;
            toast.Top = bottom - ToastHeight;
            bottom -= ToastHeight + Gap;
        }
    }
}
