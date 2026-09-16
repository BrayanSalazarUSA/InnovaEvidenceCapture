using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace InnovaEvidenceCapture.Services;

public class AppConfig
{
    [JsonPropertyName("backendUrl")] public string BackendUrl { get; set; } = "http://localhost:8080";
    [JsonPropertyName("stationCode")] public string StationCode { get; set; } = "";
    [JsonPropertyName("stationName")] public string StationName { get; set; } = "";
    [JsonPropertyName("captureRoot")] public string CaptureRoot { get; set; } = @"C:\InnovaEvidence";
    [JsonPropertyName("retentionHours")] public int RetentionHours { get; set; } = 48;
    [JsonPropertyName("uploadEnabled")] public bool UploadEnabled { get; set; } = true;
    [JsonPropertyName("hotkeyModifiers")] public string HotkeyModifiers { get; set; } = "Ctrl+Shift";
    [JsonPropertyName("hotkeyKey")] public string HotkeyKey { get; set; } = "I";

    public static AppConfig Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        AppConfig cfg;
        try
        {
            cfg = File.Exists(path)
                ? JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(path)) ?? new AppConfig()
                : new AppConfig();
        }
        catch
        {
            cfg = new AppConfig();
        }

        if (string.IsNullOrWhiteSpace(cfg.StationCode))
            cfg.StationCode = Environment.MachineName;
        if (string.IsNullOrWhiteSpace(cfg.StationName))
            cfg.StationName = cfg.StationCode;

        return cfg;
    }
}
