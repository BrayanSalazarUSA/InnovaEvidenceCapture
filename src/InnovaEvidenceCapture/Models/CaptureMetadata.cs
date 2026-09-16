using System.Text.Json.Serialization;

namespace InnovaEvidenceCapture.Models;

public class CaptureMetadata
{
    [JsonPropertyName("clientCaptureId")] public string ClientCaptureId { get; set; } = Guid.NewGuid().ToString("N")[..12];
    [JsonPropertyName("stationCode")] public string StationCode { get; set; } = "";
    [JsonPropertyName("stationName")] public string StationName { get; set; } = "";
    [JsonPropertyName("captureType")] public string CaptureType { get; set; } = "IMAGE";
    [JsonPropertyName("capturedAt")] public DateTime CapturedAt { get; set; } = DateTime.Now;
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("sizeBytes")] public long SizeBytes { get; set; }
    [JsonPropertyName("mimeType")] public string MimeType { get; set; } = "image/png";
    [JsonPropertyName("windowsUser")] public string WindowsUser { get; set; } = Environment.UserName;
    [JsonPropertyName("appVersion")] public string AppVersion { get; set; } = "0.1.0";
    [JsonPropertyName("notes")] public string? Notes { get; set; }
}
