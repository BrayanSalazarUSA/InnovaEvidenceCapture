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
    private enum Tool { None, Move, Arrow, Rect, Ellipse }

    private sealed record Annotation(Tool Tool, System.Windows.Point A, System.Windows.Point B);

    private static readonly System.Windows.Media.Color MarkColor =
        System.Windows.Media.Color.FromRgb(0xE6, 0x3C, 0x32);

    public bool Confirmed { get; private set; }

    private readonly System.Drawing.Bitmap? _bitmap;
    private double _scale = 1;

    // Tamano real del medio en pixeles. En la foto es el bitmap; en el video es
    // la miniatura, que ffmpeg saca del primer fotograma a tamano completo.
    private int _mediaWidth;
    private int _mediaHeight;
    private readonly Canvas _overlay = new();
    private readonly List<Annotation> _annotations = new();
    private readonly List<System.Windows.Controls.Button> _toolButtons = new();

    private Tool _tool = Tool.None;
    private System.Windows.Point _start;
    private bool _drawing;
    private readonly List<UIElement> _liveShapes = new();

    // Arrastre de una marca ya dibujada: indice en _annotations, punto donde se
    // agarro y como estaba la marca antes de empezar a moverla.
    private int _movingIndex = -1;
    private System.Windows.Point _moveOrigin;
    private Annotation? _moveStart;

    /// <summary>Margen para agarrar el trazo de una marca, en pixeles.</summary>
    private const double GrabTolerance = 9;

    // =====================================================================
    // FOTO
    // =====================================================================
    public PreviewWindow(System.Drawing.Bitmap bitmap, bool looksBlank)
    {
        _bitmap = bitmap;
        _mediaWidth = bitmap.Width;
        _mediaHeight = bitmap.Height;

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

        var tools = BuildToolsPanel();
        Grid.SetRow(tools, 1);
        root.Children.Add(tools);

        var frame = BuildAnnotatedStage(ToImageSource(bitmap), displayWidth, displayHeight);
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
        // La miniatura es el primer fotograma a tamano completo, asi que sirve
        // de lienzo: lo que se marca aqui cae en los mismos pixeles del video.
        BitmapImage? poster = null;
        if (thumbnailPath is not null && File.Exists(thumbnailPath))
        {
            try { poster = FromFile(thumbnailPath); } catch { poster = null; }
        }

        double displayWidth = 640;
        double displayHeight = 360;

        if (poster is not null)
        {
            _mediaWidth = poster.PixelWidth;
            _mediaHeight = poster.PixelHeight;

            const double maxWidth = 900;
            const double maxHeight = 420;
            _scale = Math.Min(Math.Min(maxWidth / _mediaWidth, maxHeight / _mediaHeight), 1.0);

            displayWidth = Math.Round(_mediaWidth * _scale);
            displayHeight = Math.Round(_mediaHeight * _scale);
        }

        Setup("Innova Evidence Capture — confirmar grabacion",
              displayWidth + 56, displayHeight + (poster is not null ? 265 : 240));

        var root = NewRoot();
        AddWarning(root, 0, false, "");

        if (poster is not null)
        {
            var tools = BuildToolsPanel();
            Grid.SetRow(tools, 1);
            root.Children.Add(tools);

            var stage = BuildAnnotatedStage(poster, displayWidth, displayHeight);
            Grid.SetRow(stage, 2);
            root.Children.Add(stage);
        }
        else
        {
            var badge = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x38, 0x22, 0x22)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 5),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10),
                Child = new TextBlock
                {
                    Text = $"⏺  Grabacion de {seconds} segundos",
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
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = "▶",
                    FontSize = 54,
                    Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x5A, 0x60, 0x6A)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                }
            };
            Grid.SetRow(preview, 2);
            root.Children.Add(preview);
        }

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

    /// <summary>Hay algo marcado que valga la pena quemar en el archivo.</summary>
    public bool HasAnnotations => _annotations.Count > 0;

    /// <summary>
    /// Escribe las marcas en un PNG transparente del tamano exacto del video,
    /// para que ffmpeg lo superponga. Devuelve false si no hay nada que marcar
    /// o si algo fallo: en ese caso el video se guarda sin marcas, nunca se
    /// pierde la grabacion por culpa de un dibujo.
    /// </summary>
    public bool TrySaveOverlayPng(string path)
    {
        if (_annotations.Count == 0 || _mediaWidth <= 0 || _mediaHeight <= 0) return false;

        try
        {
            using var canvas = new System.Drawing.Bitmap(
                _mediaWidth, _mediaHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            using (var graphics = System.Drawing.Graphics.FromImage(canvas))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                DrawAnnotations(graphics, _mediaWidth);
            }

            canvas.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            return File.Exists(path);
        }
        catch (Exception ex)
        {
            Services.LogService.Error("No se pudo preparar la capa de marcas del video", ex);
            return false;
        }
    }

    // =====================================================================
    // Estructura comun
    // =====================================================================

    /// <summary>Fila de herramientas de anotacion, igual para foto y video.</summary>
    private StackPanel BuildToolsPanel()
    {
        var tools = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 10)
        };

        tools.Children.Add(new TextBlock
        {
            Text = "Señalar:",  // arrastrar una marca ya hecha tambien se puede sin herramienta
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9A, 0x9F, 0xA8)),
            FontSize = 12.5,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });

        tools.Children.Add(ToolButton("↔  Mover", Tool.Move));
        tools.Children.Add(ToolButton("↗  Flecha", Tool.Arrow));
        tools.Children.Add(ToolButton("▭  Recuadro", Tool.Rect));
        tools.Children.Add(ToolButton("◯  Círculo", Tool.Ellipse));

        var undo = SmallButton("Deshacer");
        undo.Click += (_, _) =>
        {
            if (_annotations.Count > 0)
            {
                _annotations.RemoveAt(_annotations.Count - 1);
                Redraw();
            }
        };
        tools.Children.Add(undo);

        var clear = SmallButton("Limpiar");
        clear.Click += (_, _) => { _annotations.Clear(); Redraw(); };
        tools.Children.Add(clear);

        return tools;
    }

    /// <summary>
    /// Imagen y capa de dibujo exactamente del mismo tamano, para que las
    /// coordenadas del mouse se puedan llevar a pixeles del archivo.
    /// </summary>
    private Border BuildAnnotatedStage(ImageSource source, double displayWidth, double displayHeight)
    {
        var stage = new Grid { Width = displayWidth, Height = displayHeight };
        stage.Children.Add(new System.Windows.Controls.Image
        {
            Source = source,
            Stretch = Stretch.Fill
        });

        _overlay.Background = System.Windows.Media.Brushes.Transparent;
        _overlay.Cursor = Cursors.Cross;
        _overlay.MouseLeftButtonDown += OnDrawDown;
        _overlay.MouseMove += OnDrawMove;
        _overlay.MouseLeftButtonUp += OnDrawUp;
        stage.Children.Add(_overlay);

        return new Border
        {
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x3A, 0x3D, 0x44)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Child = stage
        };
    }


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
        _overlay.Cursor = _tool switch
        {
            Tool.None => Cursors.Arrow,
            Tool.Move => Cursors.Hand,
            _ => Cursors.Cross
        };
    }

    private void OnDrawDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(_overlay);

        // Sin herramienta activa, o con "Mover", un clic encima de una marca la
        // agarra: es lo que uno espera cuando la flecha quedo torcida.
        if (_tool is Tool.None or Tool.Move)
        {
            int index = HitTest(point);
            if (index < 0) return;

            _movingIndex = index;
            _moveOrigin = point;
            _moveStart = _annotations[index];
            _overlay.Cursor = Cursors.Hand;
            _overlay.CaptureMouse();
            return;
        }

        _start = point;
        _drawing = true;
        _overlay.CaptureMouse();
    }

    private void OnDrawMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        var point = e.GetPosition(_overlay);

        if (_movingIndex >= 0 && _moveStart is { } original)
        {
            var (dx, dy) = ClampOffset(original, point.X - _moveOrigin.X, point.Y - _moveOrigin.Y);

            _annotations[_movingIndex] = original with
            {
                A = new System.Windows.Point(original.A.X + dx, original.A.Y + dy),
                B = new System.Windows.Point(original.B.X + dx, original.B.Y + dy)
            };

            Redraw();
            return;
        }

        if (_drawing)
        {
            DrawLive(new Annotation(_tool, _start, point));
            return;
        }

        // La manito avisa que esa marca se puede arrastrar.
        if (_tool is Tool.None or Tool.Move)
        {
            _overlay.Cursor = HitTest(point) >= 0 || _tool == Tool.Move
                ? Cursors.Hand
                : Cursors.Arrow;
        }
    }

    private void OnDrawUp(object sender, MouseButtonEventArgs e)
    {
        if (_movingIndex >= 0)
        {
            _movingIndex = -1;
            _moveStart = null;
            _overlay.ReleaseMouseCapture();
            return;
        }

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

    /// <summary>
    /// Marca que esta debajo del punto, de la ultima dibujada a la primera. Se
    /// agarra por el trazo y no por el area: asi un circulo grande no bloquea
    /// lo que quedo dentro de el.
    /// </summary>
    private int HitTest(System.Windows.Point point)
    {
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            if (IsOnStroke(_annotations[i], point)) return i;
        }

        return -1;
    }

    private static bool IsOnStroke(Annotation annotation, System.Windows.Point point)
    {
        double left = Math.Min(annotation.A.X, annotation.B.X);
        double top = Math.Min(annotation.A.Y, annotation.B.Y);
        double width = Math.Abs(annotation.B.X - annotation.A.X);
        double height = Math.Abs(annotation.B.Y - annotation.A.Y);

        switch (annotation.Tool)
        {
            case Tool.Arrow:
                return DistanceToSegment(point, annotation.A, annotation.B) <= GrabTolerance;

            case Tool.Rect:
            {
                var outer = new System.Windows.Rect(
                    left - GrabTolerance, top - GrabTolerance,
                    width + 2 * GrabTolerance, height + 2 * GrabTolerance);

                var inner = new System.Windows.Rect(
                    left + GrabTolerance, top + GrabTolerance,
                    Math.Max(0, width - 2 * GrabTolerance),
                    Math.Max(0, height - 2 * GrabTolerance));

                return outer.Contains(point) && !inner.Contains(point);
            }

            case Tool.Ellipse:
            {
                double rx = width / 2;
                double ry = height / 2;
                if (rx < 1 || ry < 1) return false;

                double nx = (point.X - (left + rx)) / rx;
                double ny = (point.Y - (top + ry)) / ry;

                // Distancia aproximada al trazo, llevada de vuelta a pixeles.
                double radial = Math.Sqrt(nx * nx + ny * ny);
                return Math.Abs(radial - 1) * Math.Min(rx, ry) <= GrabTolerance;
            }
        }

        return false;
    }

    private static double DistanceToSegment(
        System.Windows.Point point, System.Windows.Point a, System.Windows.Point b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;

        if (lengthSquared < 0.0001) return (point - a).Length;

        double t = Math.Clamp(((point.X - a.X) * dx + (point.Y - a.Y) * dy) / lengthSquared, 0, 1);
        var projection = new System.Windows.Point(a.X + t * dx, a.Y + t * dy);
        return (point - projection).Length;
    }

    /// <summary>
    /// No deja sacar la marca fuera de la imagen: lo que no se ve aqui tampoco
    /// se quema en el archivo.
    /// </summary>
    private (double Dx, double Dy) ClampOffset(Annotation annotation, double dx, double dy)
    {
        double stageWidth = _overlay.ActualWidth;
        double stageHeight = _overlay.ActualHeight;
        if (stageWidth <= 0 || stageHeight <= 0) return (dx, dy);

        double left = Math.Min(annotation.A.X, annotation.B.X);
        double top = Math.Min(annotation.A.Y, annotation.B.Y);
        double right = Math.Max(annotation.A.X, annotation.B.X);
        double bottom = Math.Max(annotation.A.Y, annotation.B.Y);

        return (Clamp(dx, -left, stageWidth - right), Clamp(dy, -top, stageHeight - bottom));

        // Si la marca es mas grande que la imagen no hay margen que respetar.
        static double Clamp(double value, double min, double max) =>
            min > max ? 0 : Math.Clamp(value, min, max);
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

            DrawAnnotations(graphics, _bitmap.Width);

            Services.LogService.Info($"Anotaciones aplicadas: {_annotations.Count}");
        }
        catch (Exception ex)
        {
            Services.LogService.Error("No se pudieron aplicar las anotaciones", ex);
        }
    }

    /// <summary>
    /// Pasa las marcas de coordenadas de pantalla a pixeles del archivo. El
    /// grosor se calcula sobre el ancho real para que una flecha se vea igual
    /// de gruesa en una camara de 720p que en una de 4K.
    /// </summary>
    private void DrawAnnotations(System.Drawing.Graphics graphics, int referenceWidth)
    {
        float thickness = Math.Max(3f, referenceWidth / 350f);
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
