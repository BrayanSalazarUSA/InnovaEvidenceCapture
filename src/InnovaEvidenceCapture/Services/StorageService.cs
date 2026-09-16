using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using InnovaEvidenceCapture.Models;

namespace InnovaEvidenceCapture.Services;

public sealed class StorageService
{
    private readonly AppConfig _cfg;

    public StorageService(AppConfig cfg) => _cfg = cfg;

    public string CapturesRoot => Path.Combine(_cfg.CaptureRoot, "captures");

    public string Save(Bitmap bmp, CaptureMetadata meta)
    {
        var dir = Path.Combine(CapturesRoot, meta.CapturedAt.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(dir);

        var name = $"{meta.CapturedAt:HHmmss}_{meta.ClientCaptureId[..4]}.png";
        var file = Path.Combine(dir, name);

        bmp.Save(file, ImageFormat.Png);

        meta.Width = bmp.Width;
        meta.Height = bmp.Height;
        meta.SizeBytes = new FileInfo(file).Length;

        WriteSidecar(file, meta, uploaded: false);
        return file;
    }

    public void MarkUploaded(string imagePath, CaptureMetadata meta) => WriteSidecar(imagePath, meta, uploaded: true);

    private static void WriteSidecar(string imagePath, CaptureMetadata meta, bool uploaded)
    {
        var payload = new { meta, uploaded, updatedAt = DateTime.Now };
        File.WriteAllText(
            Path.ChangeExtension(imagePath, ".json"),
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Borra capturas viejas SOLO si ya se subieron. Lo que no subio nunca se borra.
    /// </summary>
    public void Cleanup()
    {
        try
        {
            if (!Directory.Exists(CapturesRoot)) return;
            var cutoff = DateTime.Now.AddHours(-_cfg.RetentionHours);

            foreach (var png in Directory.EnumerateFiles(CapturesRoot, "*.png", SearchOption.AllDirectories))
            {
                if (File.GetCreationTime(png) > cutoff) continue;

                var sidecar = Path.ChangeExtension(png, ".json");
                if (!File.Exists(sidecar)) continue;

                if (!File.ReadAllText(sidecar).Contains("\"uploaded\": true")) continue;

                File.Delete(png);
                File.Delete(sidecar);
            }

            foreach (var dir in Directory.EnumerateDirectories(CapturesRoot))
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
        }
        catch
        {
            // la limpieza nunca debe tumbar la app
        }
    }
}
