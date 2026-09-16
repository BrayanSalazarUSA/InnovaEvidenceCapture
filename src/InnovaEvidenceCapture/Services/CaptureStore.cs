using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using InnovaEvidenceCapture.Models;

namespace InnovaEvidenceCapture.Services;

/// <summary>
/// Las capturas en el disco del PC y su estado. Cada .png tiene al lado un
/// .json con lo que sabemos de el.
/// </summary>
public sealed class CaptureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AppConfig _cfg;

    public CaptureStore(AppConfig cfg) => _cfg = cfg;

    public string CapturesRoot => Path.Combine(_cfg.CaptureRoot, "captures");

    public string FolderFor(DateTime when) =>
        Path.Combine(CapturesRoot, when.ToString("yyyy-MM-dd"));

    // ------------------------------------------------------------- guardar

    public CaptureRecord Save(Bitmap bitmap, CaptureRecord record)
    {
        var folder = FolderFor(record.CapturedAt);
        Directory.CreateDirectory(folder);

        var shortId = record.ClientCaptureId.Length >= 4
            ? record.ClientCaptureId.Substring(0, 4)
            : Guid.NewGuid().ToString("N").Substring(0, 4);

        record.FileName = $"{record.CapturedAt:HHmmss}_{shortId}.png";
        record.ImagePath = Path.Combine(folder, record.FileName);

        bitmap.Save(record.ImagePath, ImageFormat.Png);

        record.Width = bitmap.Width;
        record.Height = bitmap.Height;
        record.SizeBytes = new FileInfo(record.ImagePath).Length;

        Update(record);
        LogService.Info($"Captura guardada: {record.FileName} ({record.Width}x{record.Height}, {record.SizeBytes} bytes)");

        return record;
    }

    public void Update(CaptureRecord record)
    {
        if (string.IsNullOrEmpty(record.SidecarPath)) return;

        record.UpdatedAt = DateTime.Now;
        try
        {
            File.WriteAllText(record.SidecarPath, JsonSerializer.Serialize(record, JsonOptions));
        }
        catch (Exception ex)
        {
            LogService.Error($"No se pudo escribir el estado de {record.FileName}", ex);
        }
    }

    // ------------------------------------------------------------- leer

    /// <summary>Capturas de hoy, la mas reciente primero.</summary>
    public List<CaptureRecord> LoadToday() => LoadSince(DateTime.Today);

    /// <summary>Todo lo que siga pendiente de subir, sin importar el dia.</summary>
    public List<CaptureRecord> LoadPending() =>
        LoadSince(DateTime.Today.AddDays(-14)).Where(r => !r.Uploaded).ToList();

    public List<CaptureRecord> LoadSince(DateTime since)
    {
        var results = new List<CaptureRecord>();
        if (!Directory.Exists(CapturesRoot)) return results;

        foreach (var folder in Directory.EnumerateDirectories(CapturesRoot))
        {
            if (!DateTime.TryParse(Path.GetFileName(folder), out var day)) continue;
            if (day.Date < since.Date) continue;

            foreach (var png in Directory.EnumerateFiles(folder, "*.png"))
            {
                var record = LoadOne(png);
                if (record is not null) results.Add(record);
            }
        }

        return results.OrderByDescending(r => r.CapturedAt).ToList();
    }

    private CaptureRecord? LoadOne(string imagePath)
    {
        var sidecar = Path.ChangeExtension(imagePath, ".json");

        try
        {
            if (File.Exists(sidecar))
            {
                var record = JsonSerializer.Deserialize<CaptureRecord>(File.ReadAllText(sidecar));
                if (record is not null)
                {
                    record.ImagePath = imagePath;
                    if (string.IsNullOrEmpty(record.FileName))
                        record.FileName = Path.GetFileName(imagePath);
                    return record;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Estado ilegible en {Path.GetFileName(sidecar)}: {ex.Message}");
        }

        // Sin sidecar legible: se muestra igual como pendiente, para que ninguna
        // captura quede invisible para el agente.
        var info = new FileInfo(imagePath);
        return new CaptureRecord
        {
            ClientCaptureId = Guid.NewGuid().ToString("N").Substring(0, 12),
            FileName = info.Name,
            ImagePath = imagePath,
            CapturedAt = info.CreationTime,
            SizeBytes = info.Length,
            StationCode = _cfg.StationCode,
            StationName = _cfg.StationName,
            Uploaded = false,
            LastError = "Sin registro de estado"
        };
    }

    // ------------------------------------------------------------- limpieza

    /// <summary>
    /// Borra capturas viejas SOLO si ya se subieron. Lo que no llego al servidor
    /// no se toca nunca, asi un corte de internet no se traduce en evidencia
    /// perdida.
    /// </summary>
    public void Cleanup()
    {
        try
        {
            if (!Directory.Exists(CapturesRoot)) return;

            var cutoff = DateTime.Now.AddHours(-_cfg.RetentionHours);
            int removed = 0;

            foreach (var record in LoadSince(DateTime.Today.AddDays(-60)))
            {
                if (!record.Uploaded) continue;
                if (record.CapturedAt > cutoff) continue;

                try
                {
                    File.Delete(record.ImagePath);
                    if (File.Exists(record.SidecarPath)) File.Delete(record.SidecarPath);
                    removed++;
                }
                catch (Exception ex)
                {
                    LogService.Warn($"No se pudo borrar {record.FileName}: {ex.Message}");
                }
            }

            foreach (var folder in Directory.EnumerateDirectories(CapturesRoot).ToList())
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any())
                    Directory.Delete(folder);
            }

            if (removed > 0)
                LogService.Info($"Limpieza: {removed} capturas subidas con mas de {_cfg.RetentionHours} h eliminadas del PC.");
        }
        catch (Exception ex)
        {
            LogService.Error("Fallo la limpieza de capturas viejas", ex);
        }
    }
}
