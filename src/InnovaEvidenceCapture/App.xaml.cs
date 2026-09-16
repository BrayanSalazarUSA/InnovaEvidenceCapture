using System.Drawing;
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
    private static Mutex? _singleInstance;

    private AppConfig _cfg = null!;
    private CaptureService _capture = null!;
    private StorageService _storage = null!;
    private UploadService _upload = null!;
    private HotkeyService? _hotkeys;
    private WinForms.NotifyIcon? _tray;
    private bool _busy;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "InnovaEvidenceCapture.SingleInstance", out bool isNew);
        if (!isNew)
        {
            System.Windows.MessageBox.Show("Innova Evidence Capture ya esta corriendo.", "Innova Evidence Capture");
            Shutdown();
            return;
        }

        _cfg = AppConfig.Load();
        _capture = new CaptureService();
        _storage = new StorageService(_cfg);
        _upload = new UploadService(_cfg);

        _storage.Cleanup();

        _tray = new WinForms.NotifyIcon
        {
            Icon = SystemIcons.Information,
            Visible = true,
            Text = $"Innova Evidence Capture — {_cfg.StationCode}"
        };

        _tray.ContextMenuStrip = new WinForms.ContextMenuStrip();
        _tray.ContextMenuStrip.Items.Add($"Estacion: {_cfg.StationCode}").Enabled = false;
        _tray.ContextMenuStrip.Items.Add("Capturar region", null, (_, _) => Dispatcher.Invoke(OnHotkey));
        _tray.ContextMenuStrip.Items.Add("Abrir carpeta de evidencias", null, (_, _) => OpenFolder());
        _tray.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("Salir", null, (_, _) => Shutdown());
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(OnHotkey);

        _hotkeys = new HotkeyService();
        bool ok = _hotkeys.Register(_cfg.HotkeyModifiers, _cfg.HotkeyKey, () => Dispatcher.Invoke(OnHotkey));

        Balloon(
            ok ? "Listo" : "Atajo no disponible",
            ok
                ? $"{_cfg.HotkeyModifiers}+{_cfg.HotkeyKey} para capturar. Estacion {_cfg.StationCode}."
                : $"Otro programa ya usa {_cfg.HotkeyModifiers}+{_cfg.HotkeyKey}. Usa el menu del icono o cambia el atajo en appsettings.json.");
    }

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

            using var bmp = _capture.Capture(region.Value);
            bool blank = CaptureService.LooksBlank(bmp);

            var preview = new PreviewWindow(bmp, blank);
            preview.ShowDialog();
            if (!preview.Confirmed) return;

            var meta = new CaptureMetadata
            {
                StationCode = _cfg.StationCode,
                StationName = _cfg.StationName,
                CaptureType = "IMAGE",
                MimeType = "image/png"
            };

            var path = _storage.Save(bmp, meta);
            Balloon("Evidencia guardada", System.IO.Path.GetFileName(path));

            var (uploaded, message) = await _upload.UploadAsync(path, meta);
            if (uploaded)
            {
                _storage.MarkUploaded(path, meta);
                Balloon("Evidencia disponible", "Ya aparece en la app movil.");
            }
            else
            {
                Balloon("Guardada localmente", $"No se pudo subir: {message}");
            }
        }
        catch (Exception ex)
        {
            Balloon("Error en la captura", ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OpenFolder()
    {
        try
        {
            System.IO.Directory.CreateDirectory(_storage.CapturesRoot);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _storage.CapturesRoot,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void Balloon(string title, string text)
    {
        if (_tray is null) return;
        _tray.BalloonTipTitle = title;
        _tray.BalloonTipText = text;
        _tray.ShowBalloonTip(4000);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkeys?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        base.OnExit(e);
    }
}
