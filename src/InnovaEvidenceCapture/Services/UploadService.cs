using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using InnovaEvidenceCapture.Models;

namespace InnovaEvidenceCapture.Services;

public sealed record UploadResult(bool Ok, long? ServerCaptureId, string Message);

/// <summary>Lo que el backend sabe de una captura, para sincronizar estados.</summary>
public sealed record RemoteCapture(long Id, string ClientCaptureId, string Status);

public sealed class UploadService
{
    private readonly AppConfig _cfg;
    private readonly HttpClient _http;

    public UploadService(AppConfig cfg)
    {
        _cfg = cfg;
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    private string BaseUrl => _cfg.BackendUrl.TrimEnd('/');

    // --------------------------------------------------------------- subir

    public async Task<UploadResult> UploadAsync(CaptureRecord record)
    {
        if (!_cfg.UploadEnabled)
            return new UploadResult(false, null, "Subida desactivada en appsettings.json");

        if (!File.Exists(record.ImagePath))
            return new UploadResult(false, null, "El archivo ya no esta en el disco");

        try
        {
            using var form = new MultipartFormDataContent();

            var bytes = await File.ReadAllBytesAsync(record.ImagePath);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(record.MimeType);
            form.Add(fileContent, "file", record.FileName);

            // Texto plano a proposito: el backend lo recibe con @RequestPart String
            // y Spring solo lo entrega tal cual si la parte NO viene como
            // application/json. Es el mismo patron que usa la app movil.
            form.Add(new StringContent(BuildMetadataJson(record), Encoding.UTF8), "metadata");

            using var response = await _http.PostAsync($"{BaseUrl}/api/evidence-captures", form);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                return new UploadResult(false, null, $"HTTP {(int)response.StatusCode}: {Trim(body)}");

            long? serverId = null;
            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("id", out var idElement)
                    && idElement.TryGetInt64(out var value))
                {
                    serverId = value;
                }
            }
            catch
            {
                // Subio bien aunque la respuesta no se pueda leer; el id llega luego
                // por la sincronizacion de estados.
            }

            return new UploadResult(true, serverId, "Subida");
        }
        catch (TaskCanceledException)
        {
            return new UploadResult(false, null, "La subida tardo demasiado");
        }
        catch (HttpRequestException ex)
        {
            return new UploadResult(false, null, $"Sin conexion con el servidor: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new UploadResult(false, null, ex.Message);
        }
    }

    private string BuildMetadataJson(CaptureRecord record)
    {
        var payload = new Dictionary<string, object?>
        {
            ["clientCaptureId"] = record.ClientCaptureId,
            ["stationCode"] = record.StationCode,
            ["stationName"] = record.StationName,
            ["captureType"] = record.CaptureType,
            // Con offset para que el backend sepa la hora real del PC.
            ["capturedAt"] = record.CapturedAt.ToString("yyyy-MM-ddTHH:mm:sszzz"),
            ["width"] = record.Width,
            ["height"] = record.Height,
            ["sizeBytes"] = record.SizeBytes,
            ["mimeType"] = record.MimeType,
            ["durationSeconds"] = record.DurationSeconds,
            ["windowsUser"] = record.WindowsUser,
            ["appVersion"] = record.AppVersion
        };

        return JsonSerializer.Serialize(payload);
    }

    // --------------------------------------------------- sincronizar estados

    /// <summary>
    /// Pregunta al backend por las capturas recientes de esta estacion, para
    /// saber cuales ya fueron usadas en un pending report.
    /// </summary>
    public async Task<List<RemoteCapture>> FetchRecentAsync(int minutes)
    {
        var results = new List<RemoteCapture>();

        try
        {
            var url = $"{BaseUrl}/api/evidence-captures/recent" +
                      $"?station={Uri.EscapeDataString(_cfg.StationCode)}&minutes={minutes}";

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var response = await _http.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode) return results;

            var body = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return results;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = item.TryGetProperty("id", out var idEl) && idEl.TryGetInt64(out var v) ? v : 0;
                var clientId = item.TryGetProperty("clientCaptureId", out var cEl) ? cEl.GetString() : null;
                var status = item.TryGetProperty("status", out var sEl) ? sEl.GetString() : null;

                if (id > 0 && !string.IsNullOrEmpty(clientId))
                    results.Add(new RemoteCapture(id, clientId!, status ?? "AVAILABLE"));
            }
        }
        catch (Exception ex)
        {
            LogService.Warn($"No se pudieron sincronizar estados: {ex.Message}");
        }

        return results;
    }

    private static string Trim(string value) =>
        string.IsNullOrEmpty(value) ? "" : (value.Length <= 300 ? value : value.Substring(0, 300));
}
