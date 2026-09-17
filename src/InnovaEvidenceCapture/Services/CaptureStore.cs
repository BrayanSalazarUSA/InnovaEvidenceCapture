using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using InnovaEvidenceCapture.Models;

namespace InnovaEvidenceCapture.Services;

/// <summary>
/// Las capturas en el disco del PC y su estado. Cada archivo tiene al lado un
/// .json (oculto) con lo que sabemos de el.
/// </summary>
public sealed class CaptureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly AppConfig _cfg;

    public CaptureStore(AppConfig cfg) => _cfg = cfg;

    /// <summary>
    /// Incluye el nombre del PC aunque cada maquina solo guarde lo suyo: si
    /// alguien copia archivos entre PCs, el origen queda claro sin abrir nada.
    /// </summary>
    public string CapturesRoot => Path.Combine(_cfg.CaptureRoot, "captures", Sanitize(_cfg.StationCode));

    public string FolderFor(DateTime when) => Path.Combine(CapturesRoot, when.ToString("yyyy-MM-dd"));

    public string TempFolder
    {
        get
        {
            var path = Path.Combine(_cfg.CaptureRoot, "temp");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    // ------------------------------------------------------------- guardar

    public CaptureRecord SaveImage(Bitmap bitmap, CaptureRecord record)
    {
        bool jpeg = !string.Equals(_cfg.ImageFormat, "png", StringComparison.OrdinalIgnoreCase);
        var target = PrepareTarget(record, jpeg ? "jpg" : "png");

        if (jpeg) SaveJpeg(bitmap, target, _cfg.JpegQuality);
        else bitmap.Save(target, ImageFormat.Png);

        record.Width = bitmap.Width;
        record.Height = bitmap.Height;
        record.SizeBytes = new FileInfo(target).Length;
        record.CaptureType = "IMAGE";
        record.MimeType = jpeg ? "image/jpeg" : "image/png";

        Update(record);
        LogService.Info($"Captura guardada: {record.FileName} ({record.Width}x{record.Height}, {Kb(record.SizeBytes)})");
        return record;
    }

    /// <summary>
    /// Guarda en JPEG con la calidad pedida. El Save() normal de .NET usa
    /// calidad 75 por defecto, que en texto de camaras (placas, horas) ya se
    /// nota, por eso hay que pasarle el parametro a mano.
    /// </summary>
    private static void SaveJpeg(Bitmap bitmap, string path, int quality)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.MimeType == "image/jpeg");

        if (codec is null)
        {
            bitmap.Save(path, ImageFormat.Jpeg);
            return;
        }

        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(
            System.Drawing.Imaging.Encoder.Quality, (long)Math.Clamp(quality, 60, 100));

        bitmap.Save(path, codec, parameters);
    }

    /// <summary>Mueve el clip recien grabado a la carpeta del dia.</summary>
    public CaptureRecord SaveVideo(string tempVideoPath, string? tempThumbPath, CaptureRecord record)
    {
        var target = PrepareTarget(record, "mp4");

        File.Move(tempVideoPath, target, overwrite: true);

        if (tempThumbPath is not null && File.Exists(tempThumbPath))
        {
            var thumb = target + ".thumb.jpg";
            File.Move(tempThumbPath, thumb, overwrite: true);
            Hide(thumb);
        }

        record.SizeBytes = new FileInfo(target).Length;
        record.CaptureType = "VIDEO";
        record.MimeType = "video/mp4";

        Update(record);
        LogService.Info($"Video guardado: {record.FileName} ({record.DurationSeconds ?? 0} s, {Kb(record.SizeBytes)})");
        return record;
    }

    private string PrepareTarget(CaptureRecord record, string extension)
    {
        var folder = FolderFor(record.CapturedAt);
        Directory.CreateDirectory(folder);

        var shortId = record.ClientCaptureId.Length >= 4
            ? record.ClientCaptureId.Substring(0, 4)
            : Guid.NewGuid().ToString("N").Substring(0, 4);

        record.FileName = $"{record.CapturedAt:HHmmss}_{shortId}.{extension}";
        record.ImagePath = Path.Combine(folder, record.FileName);
        return record.ImagePath;
    }

    public void Update(CaptureRecord record)
    {
        if (string.IsNullOrEmpty(record.SidecarPath)) return;

        record.UpdatedAt = DateTime.Now;
        try
        {
            // Se quita el atributo oculto para poder reescribirlo y se vuelve a poner.
            if (File.Exists(record.SidecarPath))
                File.SetAttributes(record.SidecarPath, FileAttributes.Normal);

            File.WriteAllText(record.SidecarPath, JsonSerializer.Serialize(record, JsonOptions));
            Hide(record.SidecarPath);
        }
        catch (Exception ex)
        {
            LogService.Error($"No se pudo escribir el estado de {record.FileName}", ex);
        }
    }

    /// <summary>El agente no tiene por que ver los archivos de estado.</summary>
    private static void Hide(string path)
    {
        try { File.SetAttributes(path, FileAttributes.Hidden); } catch { }
    }

    // ------------------------------------------------------------- leer

    public List<CaptureRecord> LoadToday() => LoadSince(DateTime.Today);

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

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension != ".png" && extension != ".jpg" && extension != ".mp4") continue;

                // La miniatura del video vive al lado como "video.mp4.thumb.jpg".
                // Sin esto se cuela como si fuera una foto que el agente tomo, y
                // la cola la sube: por cada video aparecia una imagen fantasma.
                if (file.EndsWith(".thumb.jpg", StringComparison.OrdinalIgnoreCase)) continue;

                // Lo que acompana a una captura (miniatura, sidecar) va oculto.
                // Una captura de verdad nunca lo esta.
                if (IsHidden(file)) continue;

                var record = LoadOne(file);
                if (record is not null) results.Add(record);
            }
        }

        return results.OrderByDescending(r => r.CapturedAt).ToList();
    }

    private CaptureRecord? LoadOne(string filePath)
    {
        var sidecar = Path.ChangeExtension(filePath, ".json");

        try
        {
            if (File.Exists(sidecar))
            {
                var record = JsonSerializer.Deserialize<CaptureRecord>(File.ReadAllText(sidecar));
                if (record is not null)
                {
                    record.ImagePath = filePath;
                    if (string.IsNullOrEmpty(record.FileName))
                        record.FileName = Path.GetFileName(filePath);
                    return record;
                }
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"Estado ilegible en {Path.GetFileName(sidecar)}: {ex.Message}");
        }

        // Sin sidecar legible se muestra igual como pendiente: ninguna captura
        // puede quedar invisible para el agente.
        var info = new FileInfo(filePath);
        bool isVideo = info.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);

        return new CaptureRecord
        {
            ClientCaptureId = Guid.NewGuid().ToString("N").Substring(0, 12),
            FileName = info.Name,
            ImagePath = filePath,
            CapturedAt = info.CreationTime,
            SizeBytes = info.Length,
            StationCode = _cfg.StationCode,
            StationName = _cfg.StationName,
            CaptureType = isVideo ? "VIDEO" : "IMAGE",
            MimeType = isVideo
                ? "video/mp4"
                : info.Extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    ? "image/png"
                    : "image/jpeg",
            Uploaded = false,
            LastError = "Sin registro de estado"
        };
    }

    private static bool IsHidden(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.Hidden) != 0; }
        catch { return false; }
    }

    // ------------------------------------------------------------- limpieza

    /// <summary>
    /// Borra capturas viejas SOLO si ya se subieron. Lo que no llego al servidor
    /// no se toca nunca.
    /// </summary>
    public void Cleanup()
    {
        try
        {
            CleanTemp();
            if (!Directory.Exists(CapturesRoot)) return;

            var cutoff = DateTime.Now.AddHours(-_cfg.RetentionHours);
            int removed = 0;

            foreach (var record in LoadSince(DateTime.Today.AddDays(-60)))
            {
                if (!record.Uploaded) continue;
                if (record.CapturedAt > cutoff) continue;

                try
                {
                    Delete(record.ImagePath);
                    Delete(record.SidecarPath);
                    Delete(record.ImagePath + ".thumb.jpg");
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

    /// <summary>Restos de grabaciones que quedaron a medias.</summary>
    private void CleanTemp()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(TempFolder))
            {
                if (File.GetCreationTime(file) < DateTime.Now.AddHours(-6)) File.Delete(file);
            }
        }
        catch { }
    }

    private static void Delete(string path)
    {
        if (!File.Exists(path)) return;
        File.SetAttributes(path, FileAttributes.Normal);
        File.Delete(path);
    }

    private static string Kb(long bytes) =>
        bytes >= 1024 * 1024 ? $"{bytes / 1024d / 1024d:0.#} MB" : $"{bytes / 1024d:0} KB";

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "SIN-ESTACION";
        var clean = value.Trim().Replace(" ", "_");
        foreach (var c in Path.GetInvalidFileNameChars()) clean = clean.Replace(c, '_');
        return string.IsNullOrWhiteSpace(clean) ? "SIN-ESTACION" : clean;
    }
}
