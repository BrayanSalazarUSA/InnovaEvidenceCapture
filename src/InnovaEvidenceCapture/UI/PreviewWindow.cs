using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InnovaEvidenceCapture.UI;

public sealed class PreviewWindow : Window
{
    public bool Confirmed { get; private set; }

    public PreviewWindow(Bitmap bitmap, bool looksBlank)
    {
        Title = "Innova Evidence Capture — confirmar evidencia";
        Width = Math.Min(1100, Math.Max(520, bitmap.Width / 1.4 + 80));
        Height = Math.Min(820, Math.Max(400, bitmap.Height / 1.4 + 160));
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1D, 0x21));
        Topmost = true;

        var grid = new Grid { Margin = new Thickness(16) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var warning = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7A, 0x2E, 0x2E)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = looksBlank ? Visibility.Visible : Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = "La captura salio en negro. El cliente de camaras puede estar usando aceleracion por hardware. Avisa a soporte antes de usar esta evidencia.",
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap
            }
        };
        Grid.SetRow(warning, 0);
        grid.Children.Add(warning);

        var image = new System.Windows.Controls.Image
        {
            Source = ToImageSource(bitmap),
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly
        };
        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44)),
            BorderThickness = new Thickness(1),
            Child = image
        };
        Grid.SetRow(frame, 1);
        grid.Children.Add(frame);

        var info = new TextBlock
        {
            Text = $"{bitmap.Width} × {bitmap.Height} px",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x9F, 0xA8)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var discard = MakeButton("Descartar", 0x3A, 0x3D, 0x44);
        discard.Click += (_, _) => { Confirmed = false; Close(); };

        var save = MakeButton("Guardar y subir", 0x2F, 0x7D, 0xE1);
        save.IsDefault = true;
        save.Click += (_, _) => { Confirmed = true; Close(); };

        var bar = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        DockPanel.SetDock(info, Dock.Left);
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(discard, Dock.Right);
        bar.Children.Add(info);
        bar.Children.Add(save);
        bar.Children.Add(discard);

        Grid.SetRow(bar, 2);
        grid.Children.Add(bar);

        Content = grid;
        Loaded += (_, _) => { Activate(); save.Focus(); };
    }

    private static System.Windows.Controls.Button MakeButton(string text, byte r, byte g, byte b) => new()
    {
        Content = text,
        Padding = new Thickness(18, 8, 18, 8),
        Margin = new Thickness(8, 0, 0, 0),
        Foreground = System.Windows.Media.Brushes.White,
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)),
        BorderThickness = new Thickness(0),
        FontSize = 14,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    private static BitmapImage ToImageSource(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;

        var img = new BitmapImage();
        img.BeginInit();
        img.CacheOption = BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }
}
