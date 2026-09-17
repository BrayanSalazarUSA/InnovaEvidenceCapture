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
    /// <summary>
    /// Vacio a proposito: el atajo es una sola tecla. Buscar Ctrl+Shift+I a
    /// oscuras, con una persecucion en pantalla, cuesta segundos que no hay.
    /// </summary>
    [JsonPropertyName("hotkeyModifiers")] public string HotkeyModifiers { get; set; } = "";

    /// <summary>
    /// F8 y F9 estan en todos los teclados y no los usa nada. Se descartaron
    /// F1 (ayuda), F5 (refrescar) y F11 (pantalla completa): un atajo global se
    /// traga la tecla en TODO el sistema, y el agente dejaria de poder
    /// refrescar el navegador o el cliente de camaras. Impr Pant era la otra
    /// candidata pero no esta en todos los teclados.
    /// </summary>
    [JsonPropertyName("hotkeyKey")] public string HotkeyKey { get; set; } = "F8";
    /// <summary>Atajo que va directo a grabar, sin pasar por la barra de eleccion.</summary>
    [JsonPropertyName("hotkeyVideoKey")] public string HotkeyVideoKey { get; set; } = "F9";
    /// <summary>
    /// "jpeg" o "png". Una captura de camara en PNG pesa unos 4 MB; el mismo
    /// fotograma en JPEG de calidad 92 pesa unos 400 KB y a ojo es identico.
    /// Con los escaneos de cada hora esa diferencia es el 90% del bucket.
    /// </summary>
    [JsonPropertyName("imageFormat")] public string ImageFormat { get; set; } = "jpeg";

    /// <summary>Calidad del JPEG, de 1 a 100. Por debajo de 85 ya se nota.</summary>
    [JsonPropertyName("jpegQuality")] public int JpegQuality { get; set; } = 92;

    [JsonPropertyName("videoMaxSeconds")] public int VideoMaxSeconds { get; set; } = 60;
    [JsonPropertyName("videoFps")] public int VideoFps { get; set; } = 15;

    /// <summary>
    /// Donde se guarda lo que el agente cambia desde la ventana de ajustes.
    ///
    /// No puede ser el appsettings.json de Program Files: ahi solo escribe un
    /// administrador, y el agente de monitoreo no lo es. Ese archivo queda como
    /// los valores de fabrica que reparte el instalador, y este de ProgramData
    /// los pisa.
    /// </summary>
    /// <summary>"F8" o "Ctrl+Shift+I", segun tenga modificadores o no.</summary>
    [JsonIgnore] public string HotkeyLabel => Label(HotkeyKey);
    [JsonIgnore] public string HotkeyVideoLabel => Label(HotkeyVideoKey);

    private string Label(string key) =>
        string.IsNullOrWhiteSpace(HotkeyModifiers) ? key : $"{HotkeyModifiers}+{key}";

    public static string UserConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Innova Evidence Capture",
        "config.json");

    private static string DefaultsPath =>
        Path.Combine(AppContext.BaseDirectory, "appsettings.json");

    public static AppConfig Load()
    {
        var path = File.Exists(UserConfigPath) ? UserConfigPath : DefaultsPath;
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

    /// <summary>
    /// Guarda los ajustes. Devuelve el error si no pudo, para poder decirselo
    /// al agente en vez de fingir que se guardo.
    /// </summary>
    public string? Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(UserConfigPath)!;
            Directory.CreateDirectory(folder);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(UserConfigPath, json);
            LogService.Info($"Ajustes guardados en {UserConfigPath}");
            return null;
        }
        catch (Exception ex)
        {
            LogService.Error("No se pudieron guardar los ajustes", ex);
            return ex.Message;
        }
    }
}
