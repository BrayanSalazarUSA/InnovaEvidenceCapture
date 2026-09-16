using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// Confirmar o descartar lo que se acaba de capturar.
///
/// Para las fotos permite senalar: flecha, recuadro y circulo. Se anota aqui y
/// no en el celular porque el agente tiene el contexto justo en este momento, y
/// porque marcar despues obligaria a re-subir el archivo editado.
///
/// Las anotaciones se queman en la imagen al guardar: lo que ve el investigador
/// es exactamente lo que vio el agente.
/// </summary>
public sealed class PreviewWindow : Window
{
    private enum Tool { None, Arrow, Rect, Ellipse }

    private sealed record Annotation(Tool Tool, System.Windows.Point A, System.Windows.Point B);

    private static readonly System.Windows.Media.Color MarkColor =
        System.Windows.Media.Color.FromRgb(0xE6, 0x3C, 0x32);

    public bool Confirmed { get; private set; }

    private readonly System.Drawing.Bitmap? _bitmap;
    private readonly double _scale = 1;
    private readonly Canvas _overlay = new();
    private readonly List<Annotation> _annotations = new();
    private readonly List<System.Windows.Controls.Button> _toolButtons = new();

    private Tool _tool = Tool.None;
    private System.Windows.Point _start;
    private bool _drawing;
    private readonly List<UIElement> _liveShapes = new();

    // =====================================================================
    // FOTO
    // =====================================================================
    public PreviewWindow(System.Drawing.Bitmap bitmap, bool looksBlank)
    {
        _bitmap = bitmap;

        const double maxWidth = 1120;
        const double maxHeight = 600;
        _scale = Math.Min(Math.Min(maxWidth / bitmap.Width, maxHeight / bitmap.Height), 1.0);

        double displayWidth = Math.Round(bitmap.Width * _scale);
        double displayHeight = Math.Round(bitmap.Height * _scale);

        Setup("Innova Evidence Capture — confirmar evidencia",
              displayWidth + 56, displayHeight + 210);

        var root = NewRoot();

        AddWarning(root, 0, looksBlank,
            "La captura salio en negro. El cliente de camaras puede estar usando aceleracion por hardware. " +
            "Avisa a soporte antes de usar esta evidencia.");

        // Herramientas de anotacion
        var tools = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        tools.Children.Add(new TextBlock
        {
            Text = "Señalar:",
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x9F, 0xA8)),
            FontSize = 12.5,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        tools.Children.Add(ToolButton("↗  Flecha", Tool.Arrow));
        tools.Children.Add(ToolButton("▭  Recuadro", Tool.Rect));
        tools.Children.Add(ToolButton("◯  Círculo", Tool.Ellipse));

        var undo = SmallButton("Deshacer");
        undo.Click += (_, _) => { if (_annotations.Count > 0) { _annotations.RemoveAt(_annotations.Count - 1); Redraw(); } };
        tools.Children.Add(undo);

        var clear = SmallButton("Limpiar");
        clear.Click += (_, _) => { _annotations.Clear(); Redraw(); };
        tools.Children.Add(clear);

        Grid.SetRow(tools, 1);
        root.Children.Add(tools);

        // Imagen + capa de anotacion, exactamente del mismo tamano
        var stage = new Grid { Width = displayWidth, Height = displayHeight };
        stage.Children.Add(new System.Windows.Controls.Image
        {
            Source = ToImageSource(bitmap),
            Stretch = Stretch.Fill
        });

        _overlay.Background = System.Windows.Media.Brushes.Transparent;
        _overlay.Cursor = Cursors.Cross;
        _overlay.MouseLeftButtonDown += OnDrawDown;
        _overlay.MouseMove += OnDrawMove;
        _overlay.MouseLeftButtonUp += OnDrawUp;
        stage.Children.Add(_overlay);

        var frame = new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Child = stage
        };
        Grid.SetRow(frame, 2);
        root.Children.Add(frame);

        AddFooter(root, $"{bitmap.Width} × {bitmap.Height} px", null);

        Content = root;
    }

    // =====================================================================
    // VIDEO
    // =====================================================================
    public PreviewWindow(string videoPath, string? thumbnailPath, int seconds, long sizeBytes)
    {
        Setup("Innova Evidence Capture — confirmar grabacion", 720, 600);

        var root = NewRoot();
        AddWarning(root, 0, false, "");

        var badge = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x22, 0x22)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            Child = new TextBlock
            {
                Text = $"⏺  Grabacion de {seconds} segundos  ·  lista para reproducir sin conversion",
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x9A, 0x9A)),
                FontSize = 12.5
            }
        };
        Grid.SetRow(badge, 1);
        root.Children.Add(badge);

        var preview = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x0E, 0x10, 0x13)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44)),
            BorderThickness = new Thickness(1)
        };

        if (thumbnailPath is not null && File.Exists(thumbnailPath))
        {
            preview.Child = new System.Windows.Controls.Image
            {
                Source = FromFile(thumbnailPath),
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly
            };
        }
        else
        {
            preview.Child = new TextBlock
            {
                Text = "▶",
                FontSize = 54,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5A, 0x60, 0x6A)),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
        }

        Grid.SetRow(preview, 2);
        root.Children.Add(preview);

        var play = SmallButton("Reproducir");
        play.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(videoPath) { UseShellExecute = true });
            }
            catch { }
        };

        AddFooter(root, $"{seconds} s  ·  {FormatSize(sizeBytes)}", play);

        Content = root;
    }

    // =====================================================================
    // Estructura comun
    // =====================================================================

    private void Setup(string title, double width, double height)
    {
        Title = title;
        Width = Math.Max(520, width);
        Height = Math.Max(420, height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1B, 0x1D, 0x21));
        Topmost = true;
    }

    private static Grid NewRoot()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // aviso
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // herramientas
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // pie
        return root;
    }

    private static void AddWarning(Grid root, int row, bool visible, string text)
    {
        var warning = new Border
        {
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7A, 0x2E, 0x2E)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 12),
            Visibility = visible ? Visibility.Visible : Visibility.Collapsed,
            Child = new TextBlock
            {
                Text = text,
                Foreground = System.Windows.Media.Brushes.White,
                TextWrapping = TextWrapping.Wrap
            }
        };
        Grid.SetRow(warning, row);
        root.Children.Add(warning);
    }

    private void AddFooter(Grid root, string info, System.Windows.Controls.Button? extra)
    {
        var label = new TextBlock
        {
            Text = info,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x9F, 0xA8)),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };

        var discard = BigButton("Descartar", System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44));
        discard.Click += (_, _) => { Confirmed = false; Close(); };

        var save = BigButton("Guardar y subir", System.Windows.Media.Color.FromRgb(0x2F, 0x7D, 0xE1));
        save.IsDefault = true;
        save.Click += (_, _) => { Confirmed = true; Close(); };

        var bar = new DockPanel { Margin = new Thickness(0, 14, 0, 0), LastChildFill = false };
        DockPanel.SetDock(label, Dock.Left);
        DockPanel.SetDock(save, Dock.Right);
        DockPanel.SetDock(discard, Dock.Right);
        bar.Children.Add(label);
        bar.Children.Add(save);
        bar.Children.Add(discard);

        if (extra is not null)
        {
            DockPanel.SetDock(extra, Dock.Right);
            bar.Children.Add(extra);
        }

        Grid.SetRow(bar, 3);
        root.Children.Add(bar);

        Loaded += (_, _) => { Activate(); save.Focus(); };
    }

    // =====================================================================
    // Anotaciones
    // =====================================================================

    private System.Windows.Controls.Button ToolButton(string text, Tool tool)
    {
        var button = SmallButton(text);
        button.Tag = tool;
        button.Click += (_, _) =>
        {
            _tool = _tool == tool ? Tool.None : tool;
            RefreshToolButtons();
        };
        _toolButtons.Add(button);
        return button;
    }

    private void RefreshToolButtons()
    {
        foreach (var button in _toolButtons)
        {
            bool active = button.Tag is Tool tool && tool == _tool;
            button.Background = new SolidColorBrush(active
                ? MarkColor
                : System.Windows.Media.Color.FromRgb(0x2B, 0x2F, 0x36));
        }
        _overlay.Cursor = _tool == Tool.None ? Cursors.Arrow : Cursors.Cross;
    }

    private void OnDrawDown(object sender, MouseButtonEventArgs e)
    {
        if (_tool == Tool.None) return;
        _start = e.GetPosition(_overlay);
        _drawing = true;
        _overlay.CaptureMouse();
    }

    private void OnDrawMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_drawing) return;
        DrawLive(new Annotation(_tool, _start, e.GetPosition(_overlay)));
    }

    private void OnDrawUp(object sender, MouseButtonEventArgs e)
    {
        if (!_drawing) return;
        _drawing = false;
        _overlay.ReleaseMouseCapture();

        var end = e.GetPosition(_overlay);
        if (Math.Abs(end.X - _start.X) < 6 && Math.Abs(end.Y - _start.Y) < 6)
        {
            ClearLive();
            return;
        }

        _annotations.Add(new Annotation(_tool, _start, end));
        Redraw();
    }

    private void ClearLive()
    {
        foreach (var shape in _liveShapes) _overlay.Children.Remove(shape);
        _liveShapes.Clear();
    }

    private void DrawLive(Annotation annotation)
    {
        ClearLive();
        foreach (var shape in BuildShapes(annotation))
        {
            _overlay.Children.Add(shape);
            _liveShapes.Add(shape);
        }
    }

    private void Redraw()
    {
        _overlay.Children.Clear();
        _liveShapes.Clear();

        foreach (var annotation in _annotations)
            foreach (var shape in BuildShapes(annotation))
                _overlay.Children.Add(shape);
    }

    private IEnumerable<UIElement> BuildShapes(Annotation annotation)
    {
        var brush = new SolidColorBrush(MarkColor);
        const double thickness = 3;

        double left = Math.Min(annotation.A.X, annotation.B.X);
        double top = Math.Min(annotation.A.Y, annotation.B.Y);
        double width = Math.Abs(annotation.B.X - annotation.A.X);
        double height = Math.Abs(annotation.B.Y - annotation.A.Y);

        switch (annotation.Tool)
        {
            case Tool.Rect:
                var rectangle = new System.Windows.Shapes.Rectangle
                {
                    Stroke = brush, StrokeThickness = thickness, Width = width, Height = height
                };
                Canvas.SetLeft(rectangle, left);
                Canvas.SetTop(rectangle, top);
                yield return rectangle;
                break;

            case Tool.Ellipse:
                var ellipse = new System.Windows.Shapes.Ellipse
                {
                    Stroke = brush, StrokeThickness = thickness, Width = width, Height = height
                };
                Canvas.SetLeft(ellipse, left);
                Canvas.SetTop(ellipse, top);
                yield return ellipse;
                break;

            case Tool.Arrow:
                yield return new System.Windows.Shapes.Line
                {
                    X1 = annotation.A.X, Y1 = annotation.A.Y,
                    X2 = annotation.B.X, Y2 = annotation.B.Y,
                    Stroke = brush, StrokeThickness = thickness
                };

                // Punta de flecha: dos trazos cortos girados respecto al eje.
                double angle = Math.Atan2(annotation.B.Y - annotation.A.Y, annotation.B.X - annotation.A.X);
                const double headLength = 16;
                const double spread = Math.PI / 7;

                foreach (double sign in new[] { 1.0, -1.0 })
                {
                    yield return new System.Windows.Shapes.Line
                    {
                        X1 = annotation.B.X,
                        Y1 = annotation.B.Y,
                        X2 = annotation.B.X - headLength * Math.Cos(angle + sign * spread),
                        Y2 = annotation.B.Y - headLength * Math.Sin(angle + sign * spread),
                        Stroke = brush,
                        StrokeThickness = thickness
                    };
                }
                break;
        }
    }

    /// <summary>
    /// Quema las anotaciones en el bitmap original. Se llama solo si el agente
    /// confirmo; si descarta, la imagen nunca se modifica.
    /// </summary>
    public void ApplyAnnotations()
    {
        if (_bitmap is null || _annotations.Count == 0) return;

        try
        {
            using var graphics = System.Drawing.Graphics.FromImage(_bitmap);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            float thickness = Math.Max(3f, _bitmap.Width / 350f);
            var color = System.Drawing.Color.FromArgb(MarkColor.R, MarkColor.G, MarkColor.B);

            foreach (var annotation in _annotations)
            {
                using var pen = new System.Drawing.Pen(color, thickness);

                float ax = (float)(annotation.A.X / _scale);
                float ay = (float)(annotation.A.Y / _scale);
                float bx = (float)(annotation.B.X / _scale);
                float by = (float)(annotation.B.Y / _scale);

                float left = Math.Min(ax, bx);
                float top = Math.Min(ay, by);
                float width = Math.Abs(bx - ax);
                float height = Math.Abs(by - ay);

                switch (annotation.Tool)
                {
                    case Tool.Rect:
                        graphics.DrawRectangle(pen, left, top, width, height);
                        break;
                    case Tool.Ellipse:
                        graphics.DrawEllipse(pen, left, top, width, height);
                        break;
                    case Tool.Arrow:
                        pen.CustomEndCap = new System.Drawing.Drawing2D.AdjustableArrowCap(4, 5);
                        graphics.DrawLine(pen, ax, ay, bx, by);
                        break;
                }
            }

            Services.LogService.Info($"Anotaciones aplicadas: {_annotations.Count}");
        }
        catch (Exception ex)
        {
            Services.LogService.Error("No se pudieron aplicar las anotaciones", ex);
        }
    }

    // =====================================================================
    // Utilidades
    // =====================================================================

    private static System.Windows.Controls.Button SmallButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(11, 6, 11, 6),
        Margin = new Thickness(0, 0, 6, 0),
        Foreground = System.Windows.Media.Brushes.White,
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2B, 0x2F, 0x36)),
        BorderThickness = new Thickness(0),
        FontSize = 12.5,
        Cursor = Cursors.Hand
    };

    private static System.Windows.Controls.Button BigButton(string text, System.Windows.Media.Color background) => new()
    {
        Content = text,
        Padding = new Thickness(18, 8, 18, 8),
        Margin = new Thickness(8, 0, 0, 0),
        Foreground = System.Windows.Media.Brushes.White,
        Background = new SolidColorBrush(background),
        BorderThickness = new Thickness(0),
        FontSize = 14,
        Cursor = Cursors.Hand
    };

    private static BitmapImage ToImageSource(System.Drawing.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        stream.Position = 0;

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapImage FromFile(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:0.#} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:0} KB";
        return $"{bytes} B";
    }
}
