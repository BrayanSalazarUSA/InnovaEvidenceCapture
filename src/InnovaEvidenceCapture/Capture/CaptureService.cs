using System.Drawing;

namespace InnovaEvidenceCapture.Capture;

public sealed class CaptureService
{
    private readonly IScreenCapturer _capturer = new GdiCapturer();

    public string CapturerName => _capturer.Name;

    public Bitmap Capture(Rectangle region) => _capturer.Capture(region);

    /// <summary>
    /// Red de seguridad: si el cliente de camaras usa overlay de hardware la
    /// captura sale casi toda negra. Lo detectamos y se lo avisamos al agente
    /// en el preview en vez de subir evidencia inservible.
    /// </summary>
    public static bool LooksBlank(Bitmap bmp)
    {
        const int steps = 24;
        int dark = 0, total = 0;

        for (int i = 0; i < steps; i++)
        {
            for (int j = 0; j < steps; j++)
            {
                int x = (int)((i + 0.5) * bmp.Width / steps);
                int y = (int)((j + 0.5) * bmp.Height / steps);
                if (x >= bmp.Width || y >= bmp.Height) continue;

                var c = bmp.GetPixel(x, y);
                if (c.R < 12 && c.G < 12 && c.B < 12) dark++;
                total++;
            }
        }

        return total > 0 && dark >= total * 0.98;
    }
}
