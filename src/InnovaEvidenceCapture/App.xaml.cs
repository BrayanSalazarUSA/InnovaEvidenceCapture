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
    public const string Version = "0.2.0";

    private static Mutex? _singleInstance;

    private AppConfig _cfg = null!;
    private CaptureService _capture = null!;
    private CaptureStore _store = null!;
    private UploadService _upload = null!;
    private UploadQueue _queue = null!;
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

        _capture = new CaptureService();
        _store = new CaptureStore(_cfg);
        _upload = new UploadService(_cfg);
        _queue = new UploadQueue(_store, _upload);
        _queue.Changed += () => Dispatcher.InvokeAsync(() => _capturesWindow?.Refresh());
        _queue.Notify += (title, text) => Dispatcher.InvokeAsync(() => Balloon(title, text));

        _store.Cleanup();

        BuildTray();

        _hotkeys = new HotkeyService();
        bool registered = _hotkeys.Register(_cfg.HotkeyModifiers, _cfg.HotkeyKey, () => Dispatcher.Invoke(OnHotkey));

        if (registered)
        {
            LogService.Info($"Atajo registrado: {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey}");
            Balloon("Innova Evidence Capture listo",
                $"{_cfg.HotkeyModifiers}+{_cfg.HotkeyKey} para capturar. Estacion {_cfg.StationCode}.");
        }
        else
        {
            LogService.Warn($"El atajo {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey} ya lo usa otro programa.");
            Balloon("Atajo no disponible",
                $"Otro programa usa {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey}. Usa el menu del icono o cambia el atajo en appsettings.json.");
        }
    }

    /// <summary>
    /// En 30 PCs sin nadie mirando, un error no controlado no puede cerrar el
    /// programa en silencio: se registra y la app sigue viva.
    /// </summary>
    private void InstallGlobalErrorHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogService.Error("Excepcion no controlada en la interfaz", args.Exception);
            args.Handled = true;
            Balloon("Ocurrio un error", "Se registro en el archivo de log. El programa sigue funcionando.");
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
        menu.Items.Add($"Capturar region  ({_cfg.HotkeyModifiers}+{_cfg.HotkeyKey})", null,
            (_, _) => Dispatcher.Invoke(OnHotkey));
        menu.Items.Add("Mis capturas de hoy", null, (_, _) => Dispatcher.Invoke(ShowCaptures));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add($"Version {Version}").Enabled = false;
        menu.Items.Add("Salir", null, (_, _) => Shutdown());

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowCaptures);
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
        window.CaptureRequested += () => Dispatcher.InvokeAsync(OnHotkey);
        return window;
    }

    // ------------------------------------------------------------- captura

    private async void OnHotkey()
    {
        if (_busy) return;
        _busy = true;

        try
        {
            var overlay = new SelectionOverlay();
            overlay.ShowDialog();

            var region = overlay.Result;
            if (region is null) return;

            // Dejar que el overlay desaparezca antes de capturar.
            await Task.Delay(160);

            using var bitmap = _capture.Capture(region.Value);
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

            var record = new CaptureRecord
            {
                ClientCaptureId = Guid.NewGuid().ToString("N").Substring(0, 12),
                StationCode = _cfg.StationCode,
                StationName = _cfg.StationName,
                CaptureType = "IMAGE",
                MimeType = "image/png",
                CapturedAt = DateTime.Now,
                AppVersion = Version
            };

            _store.Save(bitmap, record);
            Balloon("Evidencia guardada", $"{record.FileName} · subiendo…");

            _capturesWindow?.Refresh();
            _queue.Kick();
        }
        catch (Exception ex)
        {
            LogService.Error("Fallo el ciclo de captura", ex);
            Balloon("Error en la captura", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void Balloon(string title, string text)
    {
        if (_tray is null) return;

        try
        {
            _tray.BalloonTipTitle = title;
            _tray.BalloonTipText = text;
            _tray.ShowBalloonTip(4000);
        }
        catch
        {
            // Un globo que no sale no es motivo para romper nada.
        }
    }

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
