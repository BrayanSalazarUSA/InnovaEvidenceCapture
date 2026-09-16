using System.IO;
using System.Text.Json.Serialization;

namespace InnovaEvidenceCapture.Models;

public enum CaptureState
{
    /// <summary>Guardada en el PC, todavia no llego al servidor.</summary>
    Pending,
    /// <summary>Ya esta en el servidor y visible en la app movil.</summary>
    Uploaded,
    /// <summary>Un agente ya la uso en un pending report.</summary>
    Linked
}

/// <summary>
/// Lo que se sabe de una captura. Se guarda como un .json al lado del .png, asi
/// el estado sobrevive a que el agente cierre el programa o se reinicie el PC.
/// </summary>
public class CaptureRecord
{
    public string ClientCaptureId { get; set; } = "";
    public string FileName { get; set; } = "";
    public string StationCode { get; set; } = "";
    public string StationName { get; set; } = "";
    public string CaptureType { get; set; } = "IMAGE";
    public string MimeType { get; set; } = "image/png";
    public DateTime CapturedAt { get; set; } = DateTime.Now;

    public int Width { get; set; }
    public int Height { get; set; }
    public long SizeBytes { get; set; }
    /// <summary>Solo en video. Null en las imagenes.</summary>
    public int? DurationSeconds { get; set; }

    public string WindowsUser { get; set; } = Environment.UserName;
    public string AppVersion { get; set; } = "0.2.0";

    public bool Uploaded { get; set; }
    public int UploadAttempts { get; set; }
    public string? LastError { get; set; }
    public long? ServerCaptureId { get; set; }
    public bool Linked { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    /// <summary>Ruta del .png. No se serializa: se deduce de donde se leyo.</summary>
    [JsonIgnore]
    public string ImagePath { get; set; } = "";

    [JsonIgnore]
    public string SidecarPath =>
        string.IsNullOrEmpty(ImagePath) ? "" : Path.ChangeExtension(ImagePath, ".json");

    [JsonIgnore]
    public CaptureState State => Linked ? CaptureState.Linked
                               : Uploaded ? CaptureState.Uploaded
                               : CaptureState.Pending;

    [JsonIgnore]
    public bool IsVideo => string.Equals(CaptureType, "VIDEO", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string StateLabel => State switch
    {
        CaptureState.Linked => "Usada en un reporte",
        CaptureState.Uploaded => "Subida",
        _ => UploadAttempts > 0 ? "Pendiente, reintentando" : "Pendiente de subir"
    };
}
