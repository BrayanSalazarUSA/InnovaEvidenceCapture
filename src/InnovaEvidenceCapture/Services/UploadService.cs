using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using InnovaEvidenceCapture.Models;

namespace InnovaEvidenceCapture.Services;

public sealed class UploadService
{
    private readonly AppConfig _cfg;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(3) };

    public UploadService(AppConfig cfg) => _cfg = cfg;

    public async Task<(bool ok, string message)> UploadAsync(string filePath, CaptureMetadata meta)
    {
        if (!_cfg.UploadEnabled) return (false, "Subida desactivada en appsettings.json");

        var url = $"{_cfg.BackendUrl.TrimEnd('/')}/api/evidence-captures";

        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using var form = new MultipartFormDataContent();

                var bytes = await File.ReadAllBytesAsync(filePath);
                var fileContent = new ByteArrayContent(bytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue(meta.MimeType);
                form.Add(fileContent, "file", Path.GetFileName(filePath));

                // Texto plano a proposito: el backend lo recibe con @RequestPart String
                // y Spring solo lo entrega tal cual si la parte NO viene como
                // application/json. Es el mismo patron que usa la app movil.
                form.Add(new StringContent(JsonSerializer.Serialize(meta), Encoding.UTF8), "metadata");

                using var resp = await _http.PostAsync(url, form);
                if (resp.IsSuccessStatusCode) return (true, "Subido");

                var body = await resp.Content.ReadAsStringAsync();
                if (attempt == 3) return (false, $"HTTP {(int)resp.StatusCode}: {body}");
            }
            catch (Exception ex)
            {
                if (attempt == 3) return (false, ex.Message);
            }

            await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
        }

        return (false, "No se pudo subir");
    }
}
