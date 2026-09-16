using InnovaEvidenceCapture.Models;

namespace InnovaEvidenceCapture.Services;

/// <summary>
/// Sube en segundo plano lo que este pendiente y reintenta solo.
///
/// Es la pieza que hace que un corte de internet en la sala no se traduzca en
/// evidencia perdida: la captura se guarda siempre en el disco, y la subida se
/// reintenta hasta que entre, incluso despues de reiniciar el PC.
/// </summary>
public sealed class UploadQueue : IDisposable
{
    private static readonly TimeSpan FirstRun = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);
    private const int StatusSyncMinutes = 60 * 24;

    private readonly CaptureStore _store;
    private readonly UploadService _upload;
    private readonly System.Threading.Timer _timer;

    private int _running;
    private DateTime _lastStatusSync = DateTime.MinValue;

    /// <summary>Se dispara cuando algo cambio y la ventana deberia refrescarse.</summary>
    public event Action? Changed;

    /// <summary>
    /// Avisa cuando una captura lleva varios intentos fallidos. Las subidas que
    /// salen bien no avisan nada: el agente no necesita un cartel por archivo,
    /// el estado ya se ve en "Mis capturas de hoy".
    /// </summary>
    public event Action<string>? Failed;

    /// <summary>Intentos antes de molestar al agente con el error.</summary>
    private const int AttemptsBeforeWarning = 3;

    public UploadQueue(CaptureStore store, UploadService upload)
    {
        _store = store;
        _upload = upload;
        _timer = new System.Threading.Timer(_ => { _ = PumpAsync(); }, null, FirstRun, Interval);
    }

    /// <summary>Empuja la cola ya, sin esperar al siguiente ciclo.</summary>
    public void Kick() => _ = PumpAsync();

    public async Task PumpAsync()
    {
        // Un solo ciclo a la vez: si el anterior sigue subiendo, este se salta.
        if (Interlocked.Exchange(ref _running, 1) == 1) return;

        try
        {
            var pending = _store.LoadPending();
            if (pending.Count > 0)
            {
                LogService.Info($"Cola: {pending.Count} captura(s) pendiente(s) de subir.");
            }

            foreach (var record in pending)
            {
                var result = await _upload.UploadAsync(record);

                record.UploadAttempts++;
                if (result.Ok)
                {
                    record.Uploaded = true;
                    record.ServerCaptureId = result.ServerCaptureId;
                    record.LastError = null;
                    LogService.Info($"Subida OK: {record.FileName} (id servidor {result.ServerCaptureId})");
                }
                else
                {
                    record.LastError = result.Message;
                    LogService.Warn($"Subida fallida ({record.UploadAttempts}): {record.FileName} :: {result.Message}");

                    // Un solo aviso por captura: al llegar al tercer intento.
                    // Sigue reintentando sola, pero si el problema es la red o
                    // el backend el agente tiene que enterarse.
                    if (record.UploadAttempts == AttemptsBeforeWarning)
                    {
                        Failed?.Invoke($"No se pudo subir una evidencia: {result.Message}");
                    }
                }

                _store.Update(record);
                Changed?.Invoke();
            }

            await SyncStatusesAsync();
        }
        catch (Exception ex)
        {
            LogService.Error("Fallo el ciclo de la cola de subida", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>
    /// Marca como vinculadas las capturas que algun agente ya uso en un pending
    /// report. Es lo que pinta el punto azul en la ventana.
    /// </summary>
    private async Task SyncStatusesAsync()
    {
        if (DateTime.Now - _lastStatusSync < TimeSpan.FromMinutes(1)) return;
        _lastStatusSync = DateTime.Now;

        var remote = await _upload.FetchRecentAsync(StatusSyncMinutes);
        if (remote.Count == 0) return;

        var byClientId = remote
            .GroupBy(r => r.ClientCaptureId)
            .ToDictionary(g => g.Key, g => g.First());

        bool changed = false;

        foreach (var record in _store.LoadSince(DateTime.Today.AddDays(-3)))
        {
            if (!byClientId.TryGetValue(record.ClientCaptureId, out var match)) continue;

            bool linked = string.Equals(match.Status, "LINKED", StringComparison.OrdinalIgnoreCase);
            if (record.Linked == linked && record.ServerCaptureId == match.Id && record.Uploaded) continue;

            record.Uploaded = true;
            record.Linked = linked;
            record.ServerCaptureId = match.Id;
            _store.Update(record);
            changed = true;
        }

        if (changed) Changed?.Invoke();
    }

    public void Dispose() => _timer.Dispose();
}
