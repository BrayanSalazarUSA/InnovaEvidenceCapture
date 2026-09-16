namespace InnovaEvidenceCapture.Models;

/// <summary>
/// Que se va a capturar.
///
/// Se llama CaptureKind y no CaptureMode porque WPF ya define
/// System.Windows.Input.CaptureMode (captura del mouse). Con el mismo nombre,
/// cualquier archivo de interfaz que importe System.Windows.Input deja de
/// compilar.
/// </summary>
public enum CaptureKind
{
    /// <summary>Una imagen de la region seleccionada.</summary>
    Photo,
    /// <summary>Un clip de la region, hasta el limite configurado.</summary>
    Video
}
