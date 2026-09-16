using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InnovaEvidenceCapture.Models;
using InnovaEvidenceCapture.Services;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// "Mis capturas de hoy". Lo que el Explorador de Windows no puede dar: el
/// estado de cada captura de un vistazo.
///
/// No hay boton de eliminar a proposito. Una vez subida, la evidencia es parte
/// de la cadena de custodia y solo un supervisor puede darla de baja desde el
/// dashboard, dejando constancia de quien y por que. El momento de descartar es
/// antes de subir, en la ventana de preview.
/// </summary>
public sealed class CapturesWindow : Window
{
    private static readonly Color Bg = Color.FromRgb(0x1B, 0x1D, 0x21);
    private static readonly Color CardBg = Color.FromRgb(0x23, 0x26, 0x2C);
    private static readonly Color Line = Color.FromRgb(0x32, 0x36, 0x3E);
    private static readonly Color TextColor = Color.FromRgb(0xE9, 0xEC, 0xF1);
    private static readonly Color Muted = Color.FromRgb(0x9A, 0xA0, 0xAA);
    private static readonly Color Gold = Color.FromRgb(0xC9, 0xA1, 0x3B);

    private static readonly Color DotPending = Color.FromRgb(0xE0, 0xA0, 0x30);
    private static readonly Color DotUploaded = Color.FromRgb(0x2E, 0x9E, 0x6B);
    private static readonly Color DotLinked = Color.FromRgb(0x3B, 0x82, 0xF6);

    private const double CardWidth = 240;

    private readonly AppConfig _cfg;
    private readonly CaptureStore _store;
    private readonly UploadQueue _queue;

    private readonly TextBlock _summary = new();
    private readonly WrapPanel _grid = new();
    private readonly TextBlock _empty = new();

    public event Action? CaptureRequested;

    public CapturesWindow(AppConfig cfg, CaptureStore store, UploadQueue queue)
    {
        _cfg = cfg;
        _store = store;
        _queue = queue;

        Title = "Innova Evidence Capture — Mis capturas de hoy";
        Width = 900;
        Height = 660;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Bg);

        Content = BuildLayout();

        // Cerrar solo la esconde: reabrirla desde la bandeja es instantaneo.
        Closing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    // ------------------------------------------------------------- layout

    private UIElement BuildLayout()
    {
        var root = new Grid { Margin = new Thickness(18, 16, 18, 14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Cabecera
        var title = new TextBlock
        {
            Text = "Mis capturas de hoy",
            Foreground = new SolidColorBrush(TextColor),
            FontSize = 19,
            FontWeight = FontWeights.SemiBold
        };

        _summary.Foreground = new SolidColorBrush(Muted);
        _summary.FontSize = 12.5;
        _summary.Margin = new Thickness(0, 3, 0, 0);

        var header = new StackPanel();
        header.Children.Add(title);
        header.Children.Add(_summary);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Barra de acciones
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 12)
        };

        var capture = Button("Capturar ahora", Gold, primary: true);
        capture.Click += (_, _) => { Hide(); CaptureRequested?.Invoke(); };

        var refresh = Button("Actualizar", Line);
        refresh.Click += (_, _) => Refresh();

        var retry = Button("Reintentar pendientes", Line);
        retry.Click += (_, _) => { _queue.Kick(); Refresh(); };

        var folder = Button("Abrir carpeta", Line);
        folder.Click += (_, _) => OpenPath(_store.CapturesRoot);

        var logs = Button("Ver registro", Line);
        logs.Click += (_, _) => OpenPath(LogService.Folder);

        toolbar.Children.Add(capture);
        toolbar.Children.Add(refresh);
        toolbar.Children.Add(retry);
        toolbar.Children.Add(folder);
        toolbar.Children.Add(logs);
        Grid.SetRow(toolbar, 1);
        root.Children.Add(toolbar);

        // Cuadricula
        _grid.Orientation = Orientation.Horizontal;

        _empty.Foreground = new SolidColorBrush(Muted);
        _empty.FontSize = 14;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.Margin = new Thickness(40, 70, 40, 0);
        _empty.Text = "Todavia no has capturado nada hoy.\n\n" +
                      $"Presiona {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey} frente a las camaras " +
                      "y selecciona la region que quieres guardar.";
        _empty.Visibility = Visibility.Collapsed;

        var stack = new StackPanel();
        stack.Children.Add(_empty);
        stack.Children.Add(_grid);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = stack
        };
        Grid.SetRow(scroll, 2);
        root.Children.Add(scroll);

        // Leyenda
        var legend = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 14, 0, 0)
        };
        legend.Children.Add(LegendItem(DotPending, "Pendiente de subir"));
        legend.Children.Add(LegendItem(DotUploaded, "Subida"));
        legend.Children.Add(LegendItem(DotLinked, "Usada en un reporte"));

        var note = new TextBlock
        {
            Text = "La evidencia subida no se borra desde aqui. Para darla de baja, un supervisor lo hace desde el dashboard.",
            Foreground = new SolidColorBrush(Muted),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 0)
        };

        var footer = new StackPanel();
        footer.Children.Add(legend);
        footer.Children.Add(note);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        return root;
    }

    private static UIElement LegendItem(Color color, string label)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 20, 0)
        };
        panel.Children.Add(Dot(color));
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Muted),
            FontSize = 11.5,
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private static System.Windows.Shapes.Ellipse Dot(Color color) => new()
    {
        Width = 9,
        Height = 9,
        Fill = new SolidColorBrush(color),
        VerticalAlignment = VerticalAlignment.Center
    };

    private static System.Windows.Controls.Button Button(string text, Color background, bool primary = false) => new()
    {
        Content = text,
        Padding = new Thickness(14, 7, 14, 7),
        Margin = new Thickness(0, 0, 8, 0),
        Foreground = new SolidColorBrush(primary ? Color.FromRgb(0x20, 0x1A, 0x00) : TextColor),
        Background = new SolidColorBrush(background),
        BorderThickness = new Thickness(0),
        FontSize = 13,
        FontWeight = primary ? FontWeights.SemiBold : FontWeights.Normal,
        Cursor = System.Windows.Input.Cursors.Hand
    };

    // ------------------------------------------------------------- datos

    public void ShowAndRefresh()
    {
        Refresh();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    public void Refresh()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Refresh);
            return;
        }

        List<CaptureRecord> records;
        try
        {
            records = _store.LoadToday();
        }
        catch (Exception ex)
        {
            LogService.Error("No se pudieron leer las capturas de hoy", ex);
            records = new List<CaptureRecord>();
        }

        _grid.Children.Clear();

        int pending = records.Count(r => r.State == CaptureState.Pending);
        int linked = records.Count(r => r.State == CaptureState.Linked);

        _summary.Text = records.Count == 0
            ? $"Estacion {_cfg.StationCode}"
            : $"Estacion {_cfg.StationCode}  ·  {records.Count} captura{(records.Count == 1 ? "" : "s")}" +
              (pending > 0 ? $"  ·  {pending} pendiente{(pending == 1 ? "" : "s")} de subir" : "  ·  todas subidas") +
              (linked > 0 ? $"  ·  {linked} en reportes" : "");

        _empty.Visibility = records.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var record in records)
        {
            _grid.Children.Add(BuildCard(record));
        }
    }

    private UIElement BuildCard(CaptureRecord record)
    {
        var card = new Border
        {
            Width = CardWidth,
            Margin = new Thickness(0, 0, 12, 12),
            Background = new SolidColorBrush(CardBg),
            BorderBrush = new SolidColorBrush(Line),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10)
        };

        var content = new StackPanel();

        // Miniatura
        var thumbHost = new Border
        {
            Height = 140,
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x19)),
            CornerRadius = new CornerRadius(10, 10, 0, 0),
            ClipToBounds = true
        };

        var source = LoadThumbnail(record.ImagePath, (int)CardWidth);
        if (source is not null)
        {
            thumbHost.Child = new System.Windows.Controls.Image
            {
                Source = source,
                Stretch = Stretch.UniformToFill
            };
        }
        else
        {
            thumbHost.Child = new TextBlock
            {
                Text = "sin vista previa",
                Foreground = new SolidColorBrush(Muted),
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        content.Children.Add(thumbHost);

        // Datos
        var body = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };

        var stateRow = new StackPanel { Orientation = Orientation.Horizontal };
        stateRow.Children.Add(Dot(record.State switch
        {
            CaptureState.Linked => DotLinked,
            CaptureState.Uploaded => DotUploaded,
            _ => DotPending
        }));
        stateRow.Children.Add(new TextBlock
        {
            Text = record.StateLabel,
            Foreground = new SolidColorBrush(TextColor),
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        body.Children.Add(stateRow);

        body.Children.Add(new TextBlock
        {
            Text = $"{record.CapturedAt:HH:mm}  ·  {record.Width}×{record.Height}  ·  {FormatSize(record.SizeBytes)}",
            Foreground = new SolidColorBrush(Muted),
            FontSize = 11.5,
            Margin = new Thickness(0, 5, 0, 0)
        });

        if (record.State == CaptureState.Pending && !string.IsNullOrEmpty(record.LastError))
        {
            body.Children.Add(new TextBlock
            {
                Text = record.LastError,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x8A)),
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 30,
                Margin = new Thickness(0, 5, 0, 0)
            });
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0)
        };

        var copy = Button("Copiar", Line);
        copy.FontSize = 11.5;
        copy.Padding = new Thickness(10, 5, 10, 5);
        copy.Click += (_, _) => CopyToClipboard(record);

        var open = Button("Abrir", Line);
        open.FontSize = 11.5;
        open.Padding = new Thickness(10, 5, 10, 5);
        open.Click += (_, _) => RevealInExplorer(record.ImagePath);

        actions.Children.Add(copy);
        actions.Children.Add(open);
        body.Children.Add(actions);

        content.Children.Add(body);
        card.Child = content;
        return card;
    }

    // ------------------------------------------------------------- acciones

    private static BitmapImage? LoadThumbnail(string path, int width)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.DecodePixelWidth = width;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private void CopyToClipboard(CaptureRecord record)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(record.ImagePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            Clipboard.SetImage(image);
            LogService.Info($"Copiada al portapapeles: {record.FileName}");
        }
        catch (Exception ex)
        {
            LogService.Error($"No se pudo copiar {record.FileName}", ex);
            MessageBox.Show("No se pudo copiar la imagen al portapapeles.",
                "Innova Evidence Capture", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static void RevealInExplorer(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"No se pudo abrir el explorador: {ex.Message}");
        }
    }

    private static void OpenPath(string folder)
    {
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogService.Warn($"No se pudo abrir {folder}: {ex.Message}");
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024) return $"{bytes / 1024d / 1024d:0.#} MB";
        if (bytes >= 1024) return $"{bytes / 1024d:0} KB";
        return $"{bytes} B";
    }
}
