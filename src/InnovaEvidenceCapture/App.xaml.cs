using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using InnovaEvidenceCapture.Capture;
using InnovaEvidenceCapture.Models;
using InnovaEvidenceCapture.Services;
using InnovaEvidenceCapture.UI;
using WinForms = System.Windows.Forms;

namespace InnovaEvidenceCapture;

public partial class App : System.Windows.Application
{
    public const string Version = "0.4.0";

    private static Mutex? _singleInstance;

    private AppConfig _cfg = null!;
    private CaptureService _capture = null!;
    private CaptureStore _store = null!;
    private UploadService _upload = null!;
    private UploadQueue _queue = null!;
    private VideoRecorder _recorder = null!;
    private HotkeyService? _hotkeys;
    private WinForms.NotifyIcon? _tray;
    private CapturesWindow? _capturesWindow;
    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "InnovaEvidenceCapture.SingleInstance", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show(
                "Innova Evidence Capture ya esta corriendo. Busca el icono junto al reloj.",
                "Innova Evidence Capture");
            Shutdown();
            return;
        }

        InstallGlobalErrorHandlers();

        _cfg = AppConfig.Load();
        LogService.Configure(_cfg.CaptureRoot);
        LogService.Info($"===== Inicio v{Version} · estacion {_cfg.StationCode} · usuario {Environment.UserName} =====");
        LogService.Info($"Backend: {_cfg.BackendUrl} · retencion {_cfg.RetentionHours} h");
        LogService.Info(VideoRecorder.IsAvailable
            ? $"Video disponible (ffmpeg encontrado): maximo {_cfg.VideoMaxSeconds} s a {_cfg.VideoFps} fps"
            : "Video NO disponible: falta ffmpeg.exe junto al programa");

        _capture = new CaptureService();
        _store = new CaptureStore(_cfg);
        _upload = new UploadService(_cfg);
        _queue = new UploadQueue(_store, _upload);
        _recorder = new VideoRecorder(_cfg);

        _queue.Changed += () => Dispatcher.InvokeAsync(() => _capturesWindow?.Refresh());
        _queue.Failed += message => Dispatcher.InvokeAsync(() => Notice(message, isError: true));

        _store.Cleanup();

        BuildTray();
        RegisterHotkeys();
    }

    private void RegisterHotkeys()
    {
        _hotkeys = new HotkeyService();

        bool photo = _hotkeys.Register(_cfg.HotkeyModifiers, _cfg.HotkeyKey,
            () => Dispatcher.Invoke(() => OnCapture(null)));

        bool video = _hotkeys.Register(_cfg.HotkeyModifiers, _cfg.HotkeyVideoKey,
            () => Dispatcher.Invoke(() => OnCapture(CaptureKind.Video)));

        if (photo)
        {
            LogService.Info($"Atajos: {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey} (capturar), " +
                            $"{_cfg.HotkeyModifiers}+{_cfg.HotkeyVideoKey} (grabar: {(video ? "ok" : "ocupado")})");
            Notice($"Listo · {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey} para capturar · {_cfg.StationCode}");
        }
        else
        {
            LogService.Warn($"El atajo {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey} ya lo usa otro programa.");
            Notice($"Otro programa usa {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey}. Usa el menu del icono.",
                isError: true);
        }
    }

    private void InstallGlobalErrorHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Error("Excepcion no controlada en la interfaz", args.Exception);
            args.Handled = true;
            Notice("Ocurrio un error. Quedo en el log; el programa sigue funcionando.", isError: true);
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogService.Error("Excepcion no controlada", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogService.Error("Tarea en segundo plano fallida", args.Exception);
            args.SetObserved();
        };
    }

    private void BuildTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadAppIcon(),
            Visible = true,
            Text = $"Innova Evidence Capture — {_cfg.StationCode}"
        };

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add($"Estacion: {_cfg.StationCode}").Enabled = false;
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add($"Capturar  ({_cfg.HotkeyModifiers}+{_cfg.HotkeyKey})", null,
            (_, _) => Dispatcher.Invoke(() => OnCapture(null)));
        menu.Items.Add($"Grabar video  ({_cfg.HotkeyModifiers}+{_cfg.HotkeyVideoKey})", null,
            (_, _) => Dispatcher.Invoke(() => OnCapture(CaptureKind.Video)));
        menu.Items.Add("Mis capturas de hoy", null, (_, _) => Dispatcher.Invoke(ShowCaptures));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add($"Version {Version}").Enabled = false;
        menu.Items.Add("Salir", null, (_, _) => Shutdown());

        _tray.ContextMenuStrip = menu;

        // Un clic normal abre el menu: el agente no tiene que acordarse de usar
        // el clic derecho.
        _tray.MouseUp += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left) ShowTrayMenu();
        };
    }

    /// <summary>
    /// Se invoca el mismo metodo interno que usa el clic derecho para que el
    /// menu se cierre solo al hacer clic fuera. Llamar a Show() directamente lo
    /// deja pegado en pantalla.
    /// </summary>
    private void ShowTrayMenu()
    {
        if (_tray?.ContextMenuStrip is null) return;

        try
        {
            var method = typeof(WinForms.NotifyIcon).GetMethod(
                "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

            if (method is not null)
            {
                method.Invoke(_tray, null);
                return;
            }
        }
        catch { }

        _tray.ContextMenuStrip.Show(WinForms.Cursor.Position);
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("app.ico", StringComparison.OrdinalIgnoreCase));

            if (name is not null)
            {
                using Stream? stream = assembly.GetManifestResourceStream(name);
                if (stream is not null) return new Icon(stream);
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"No se pudo cargar el icono propio: {ex.Message}");
        }

        return SystemIcons.Application;
    }

    private void ShowCaptures()
    {
        _capturesWindow ??= BuildCapturesWindow();
        _capturesWindow.ShowAndRefresh();
    }

    private CapturesWindow BuildCapturesWindow()
    {
        var window = new CapturesWindow(_cfg, _store, _queue);
        window.CaptureRequested += () => Dispatcher.InvokeAsync(() => OnCapture(null));
        return window;
    }

    // ------------------------------------------------------------- captura

    private async void OnCapture(CaptureKind? directMode)
    {
        if (_busy) return;
        _busy = true;

        try
        {
            var overlay = new SelectionOverlay(directMode);
            overlay.ShowDialog();

            if (overlay.Region is null) return;
            var region = overlay.Region.Value;

            // Dejar que el overlay desaparezca antes de capturar o grabar.
            await Task.Delay(160);

            if (overlay.Mode == CaptureKind.Video) await CaptureVideoAsync(region);
            else CapturePhoto(region);
        }
        catch (Exception ex)
        {
            LogService.Error("Fallo el ciclo de captura", ex);
            Notice($"Error en la captura: {ex.Message}", isError: true);
        }
        finally
        {
            _busy = false;
        }
    }

    private void CapturePhoto(System.Drawing.Rectangle region)
    {
        using var bitmap = _capture.Capture(region);
        bool blank = CaptureService.LooksBlank(bitmap);

        if (blank)
        {
            LogService.Warn($"Captura en negro detectada ({bitmap.Width}x{bitmap.Height}). " +
                            "Posible overlay de hardware en el cliente de camaras.");
        }

        var preview = new PreviewWindow(bitmap, blank);
        preview.ShowDialog();

        if (!preview.Confirmed)
        {
            LogService.Info("Captura descartada por el agente.");
            return;
        }

        // Las marcas se queman solo si el agente confirmo.
        preview.ApplyAnnotations();

        var record = NewRecord("IMAGE", "image/png");
        _store.SaveImage(bitmap, record);

        Notice("Evidencia guardada");
        _capturesWindow?.Refresh();
        _queue.Kick();
    }

    private async Task CaptureVideoAsync(System.Drawing.Rectangle region)
    {
        if (!VideoRecorder.IsAvailable)
        {
            LogService.Warn("Se pidio grabar pero no hay ffmpeg.exe junto al programa.");
            Notice("Falta ffmpeg.exe: reinstala el paquete completo para grabar video.", isError: true);
            return;
        }

        var record = NewRecord("VIDEO", "video/mp4");
        var tempPath = Path.Combine(_store.TempFolder, $"rec_{record.ClientCaptureId}.mp4");

        var bar = new RecordingBar(_cfg.VideoMaxSeconds);
        using var stop = new CancellationTokenSource();
        bar.StopRequested += () => stop.Cancel();
        bar.Show();

        RecordingResult result;
        try
        {
            result = await _recorder.RecordAsync(
                region, tempPath, _cfg.VideoMaxSeconds, _cfg.VideoFps, stop.Token,
                elapsed => Dispatcher.InvokeAsync(() => bar.UpdateElapsed(elapsed)));
        }
        finally
        {
            bar.Close();
        }

        if (!result.Ok || result.VideoPath is null)
        {
            Notice($"No se pudo grabar: {result.Message}", isError: true);
            return;
        }

        long size = new FileInfo(result.VideoPath).Length;

        var preview = new PreviewWindow(result.VideoPath, result.ThumbnailPath, result.DurationSeconds, size);
        preview.ShowDialog();

        if (!preview.Confirmed)
        {
            LogService.Info("Grabacion descartada por el agente.");
            TryDelete(result.VideoPath);
            TryDelete(result.ThumbnailPath);
            return;
        }

        var videoPath = result.VideoPath;
        var thumbnailPath = result.ThumbnailPath;

        // Las marcas se queman en el video antes de guardarlo. Recodificar toma
        // unos segundos, por eso solo pasa si el agente dibujo algo.
        if (preview.HasAnnotations)
        {
            var overlay = Path.Combine(_store.TempFolder, $"marcas_{record.ClientCaptureId}.png");

            if (preview.TrySaveOverlayPng(overlay))
            {
                Notice("Marcando el video…");

                var burned = await _recorder.BurnOverlayAsync(videoPath, overlay);

                if (burned.Ok)
                {
                    TryDelete(videoPath);
                    TryDelete(thumbnailPath);
                    videoPath = burned.VideoPath;
                    thumbnailPath = burned.ThumbnailPath;
                }
                else
                {
                    // Se guarda igual, sin marcas: la grabacion no se pierde.
                    Notice("El video se guardo sin las marcas", isError: true);
                }

                TryDelete(overlay);
            }
        }

        record.DurationSeconds = result.DurationSeconds;
        _store.SaveVideo(videoPath, thumbnailPath, record);

        Notice($"Grabacion guardada · {result.DurationSeconds} s");
        _capturesWindow?.Refresh();
        _queue.Kick();
    }

    private CaptureRecord NewRecord(string type, string mime) => new()
    {
        ClientCaptureId = Guid.NewGuid().ToString("N").Substring(0, 12),
        StationCode = _cfg.StationCode,
        StationName = _cfg.StationName,
        CaptureType = type,
        MimeType = mime,
        CapturedAt = DateTime.Now,
        AppVersion = Version
    };

    private static void TryDelete(string? path)
    {
        try { if (path is not null && File.Exists(path)) File.Delete(path); } catch { }
    }

    /// <summary>
    /// Aviso corto en la esquina. Ya no usa los globos de Windows: ocupaban
    /// media pantalla y se quedaban apilados en el centro de notificaciones.
    /// </summary>
    private void Notice(string text, bool isError = false) => Toast.Notify(text, isError);

    protected override void OnExit(ExitEventArgs e)
    {
        LogService.Info("===== Cierre =====");

        _queue?.Dispose();
        _hotkeys?.Dispose();

        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        base.OnExit(e);
    }
}
