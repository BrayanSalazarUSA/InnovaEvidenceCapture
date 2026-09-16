using System.Drawing;

namespace InnovaEvidenceCapture.Capture;

public sealed class GdiCapturer : IScreenCapturer
{
    public string Name => "GDI";

    public Bitmap Capture(Rectangle region)
    {
        var bmp = new Bitmap(region.Width, region.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Left, region.Top, 0, 0, region.Size, CopyPixelOperation.SourceCopy);
        return bmp;
    }
}
