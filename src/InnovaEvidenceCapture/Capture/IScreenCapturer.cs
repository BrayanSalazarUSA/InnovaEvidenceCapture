using System.Drawing;

namespace InnovaEvidenceCapture.Capture;

/// <summary>
/// Abstraccion de la captura de pantalla. GDI hoy; si algun PC muestra el video
/// en negro (overlay de hardware en iVMS/SmartPSS) se agrega DxgiCapturer sin
/// tocar el resto de la app.
/// </summary>
public interface IScreenCapturer
{
    string Name { get; }
    Bitmap Capture(Rectangle regionPhysicalPx);
}
