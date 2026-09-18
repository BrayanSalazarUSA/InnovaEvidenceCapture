using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using InnovaEvidenceCapture.Services;

namespace InnovaEvidenceCapture.UI;

/// <summary>
/// Saca una ventana propia de las capturas de pantalla.
///
/// El problema: ffmpeg graba el escritorio con gdigrab, o sea graba lo que se ve.
/// Nuestra barra de "Grabando 00:12" esta siempre encima, asi que quedaba quemada
/// dentro del video de evidencia.
///
/// Windows tiene una solucion exacta para esto: SetWindowDisplayAffinity con
/// WDA_EXCLUDEFROMCAPTURE. La ventana la sigue viendo el agente en el monitor,
/// pero desaparece para cualquier cosa que capture la pantalla (gdigrab, Teams,
/// OBS, Recorte de Windows). Existe desde Windows 10 2004 (build 19041); en algo
/// mas viejo la llamada falla y devuelve false, y ahi entra el plan B: mover la
/// barra fuera del rectangulo que se esta grabando.
/// </summary>
internal static class CaptureShield
{
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    private static bool _warned;

    /// <summary>
    /// Devuelve true si la ventana quedo invisible para las capturas.
    /// Llamar desde SourceInitialized (antes no existe el handle).
    /// </summary>
    public static bool Hide(Window window)
    {
        try
        {
            var helper = new WindowInteropHelper(window);
            IntPtr hwnd = helper.Handle;
            if (hwnd == IntPtr.Zero) hwnd = helper.EnsureHandle();
            if (hwnd == IntPtr.Zero) return false;

            if (SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)) return true;

            int error = Marshal.GetLastWin32Error();

            if (!_warned)
            {
                _warned = true;
                LogService.Warn(
                    $"Esta version de Windows no puede ocultar ventanas de la captura (error {error}). " +
                    "La barra de grabacion se va a mover fuera del area grabada.");
            }

            return false;
        }
        catch (Exception ex)
        {
            if (!_warned)
            {
                _warned = true;
                LogService.Warn($"No se pudo ocultar la ventana de la captura: {ex.Message}");
            }

            return false;
        }
    }
}
