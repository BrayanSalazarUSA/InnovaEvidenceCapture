using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;

namespace InnovaEvidenceCapture.Services;

public sealed record RecordingResult(
    bool Ok,
    string? VideoPath,
    string? ThumbnailPath,
    int DurationSeconds,
    string Message);

/// <summary>
/// Graba una region de la pantalla a MP4 usando ffmpeg.
///
/// Se graba directamente en H.264 + yuv420p + faststart, que es el formato que
/// reproducen el navegador y el celular sin tocar nada. Por eso estos clips NO
/// necesitan pasar por MediaConvert, a diferencia de los videos exportados del
/// playback de iVMS, que vienen con codecs que si hay que reconvertir.
/// </summary>
public sealed class VideoRecorder
{
    private readonly AppConfig _cfg;

    public VideoRecorder(AppConfig cfg) => _cfg = cfg;

    /// <summary>Busca ffmpeg.exe junto al programa; si no, en el PATH.</summary>
    public static string? FindFfmpeg()
    {
        var baseDir = AppContext.BaseDirectory;

        var candidates = new[]
        {
            Path.Combine(baseDir, "ffmpeg.exe"),
            Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
            Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe")
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate)) return candidate;

        try
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch { }

        return null;
    }

    public static bool IsAvailable => FindFfmpeg() is not null;

    /// <summary>
    /// Graba hasta <paramref name="maxSeconds"/>. Se detiene antes si se cancela
    /// el token: ahi se le manda "q" a ffmpeg para que cierre el archivo bien.
    /// Matarlo a la fuerza dejaria un MP4 sin indice, irreproducible.
    /// </summary>
    public async Task<RecordingResult> RecordAsync(
        Rectangle region,
        string outputPath,
        int maxSeconds,
        int fps,
        CancellationToken stopToken,
        Action<TimeSpan>? onTick = null)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null)
        {
            return new RecordingResult(false, null, null, 0,
                "No se encontro ffmpeg.exe junto al programa. Reinstala el paquete completo.");
        }

        // yuv420p exige ancho y alto pares.
        int width = region.Width - (region.Width % 2);
        int height = region.Height - (region.Height % 2);
        if (width < 32 || height < 32)
            return new RecordingResult(false, null, null, 0, "La region es demasiado pequena para grabar.");

        var psi = new ProcessStartInfo(ffmpeg)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardError = true
        };

        foreach (var arg in new[]
        {
            "-hide_banner", "-loglevel", "error",
            "-f", "gdigrab",
            "-framerate", fps.ToString(),
            "-draw_mouse", "0",
            "-offset_x", region.X.ToString(),
            "-offset_y", region.Y.ToString(),
            "-video_size", $"{width}x{height}",
            "-i", "desktop",
            "-t", maxSeconds.ToString(),
            "-c:v", "libx264",
            "-preset", "veryfast",
            "-crf", "23",
            "-pix_fmt", "yuv420p",
            "-movflags", "+faststart",
            "-y", outputPath
        })
        {
            psi.ArgumentList.Add(arg);
        }

        LogService.Info($"Grabando {width}x{height} en ({region.X},{region.Y}) a {fps} fps, maximo {maxSeconds} s");

        var startedAt = DateTime.Now;
        var errors = new StringBuilder();

        try
        {
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) errors.AppendLine(e.Data); };

            if (!process.Start())
                return new RecordingResult(false, null, null, 0, "No se pudo iniciar ffmpeg.");

            process.BeginErrorReadLine();

            bool stoppedByUser = false;

            while (!process.HasExited)
            {
                if (stopToken.IsCancellationRequested && !stoppedByUser)
                {
                    stoppedByUser = true;
                    try
                    {
                        await process.StandardInput.WriteAsync("q");
                        await process.StandardInput.FlushAsync();
                    }
                    catch { /* si ya cerro, el WaitForExit lo resuelve */ }
                }

                onTick?.Invoke(DateTime.Now - startedAt);
                await Task.Delay(200);
            }

            await process.WaitForExitAsync();

            int seconds = (int)Math.Round((DateTime.Now - startedAt).TotalSeconds);

            if (!File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            {
                LogService.Error($"ffmpeg no produjo archivo. Salida: {errors}");
                return new RecordingResult(false, null, null, 0,
                    "La grabacion no produjo ningun archivo. Revisa el registro.");
            }

            var thumbnail = await CreateThumbnailAsync(ffmpeg, outputPath);

            LogService.Info($"Grabacion terminada: {seconds} s, {new FileInfo(outputPath).Length / 1024} KB");
            return new RecordingResult(true, outputPath, thumbnail, seconds, "Grabacion lista");
        }
        catch (Exception ex)
        {
            LogService.Error("Fallo la grabacion", ex);
            return new RecordingResult(false, null, null, 0, ex.Message);
        }
    }

    /// <summary>
    /// Quema las marcas del agente en el video superponiendo un PNG
    /// transparente del mismo tamano.
    ///
    /// Hay que recodificar: no existe forma de pintar encima de un H.264 sin
    /// volver a comprimirlo. Por eso solo se hace cuando el agente marco algo,
    /// y si falla se devuelve el video original sin marcas: perder la flecha es
    /// molesto, perder la grabacion no es negociable.
    /// </summary>
    public async Task<(bool Ok, string VideoPath, string? ThumbnailPath)> BurnOverlayAsync(
        string videoPath, string overlayPng)
    {
        var ffmpeg = FindFfmpeg();
        if (ffmpeg is null) return (false, videoPath, null);

        var marked = Path.Combine(
            Path.GetDirectoryName(videoPath) ?? ".",
            Path.GetFileNameWithoutExtension(videoPath) + "_marcado.mp4");

        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            foreach (var arg in new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-i", videoPath,
                "-i", overlayPng,
                "-filter_complex", "[0:v][1:v]overlay=0:0:format=auto",
                "-c:v", "libx264",
                "-preset", "veryfast",
                "-crf", "23",
                "-pix_fmt", "yuv420p",
                "-movflags", "+faststart",
                "-an",
                "-y", marked
            })
            {
                psi.ArgumentList.Add(arg);
            }

            var errors = new StringBuilder();
            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.ErrorDataReceived += (_, e) => { if (e.Data is not null) errors.AppendLine(e.Data); };

            if (!process.Start()) return (false, videoPath, null);
            process.BeginErrorReadLine();
            await process.WaitForExitAsync();

            if (!File.Exists(marked) || new FileInfo(marked).Length == 0)
            {
                LogService.Warn($"No se pudieron quemar las marcas en el video. Salida: {errors}");
                return (false, videoPath, null);
            }

            // La miniatura se rehace del video marcado para que la flecha
            // tambien se vea en la galeria del celular y en el dashboard.
            var thumbnail = await CreateThumbnailAsync(ffmpeg, marked);

            LogService.Info($"Marcas quemadas en el video: {Path.GetFileName(marked)}");
            return (true, marked, thumbnail);
        }
        catch (Exception ex)
        {
            LogService.Error("Fallo al quemar las marcas en el video", ex);
            return (false, videoPath, null);
        }
    }

    /// <summary>Primer fotograma, para que el clip se vea en la galeria.</summary>
    private static async Task<string?> CreateThumbnailAsync(string ffmpeg, string videoPath)
    {
        var thumbnail = Path.ChangeExtension(videoPath, ".thumb.jpg");

        try
        {
            var psi = new ProcessStartInfo(ffmpeg)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            foreach (var arg in new[]
            {
                "-hide_banner", "-loglevel", "error",
                "-i", videoPath,
                "-frames:v", "1",
                "-q:v", "4",
                "-y", thumbnail
            })
            {
                psi.ArgumentList.Add(arg);
            }

            using var process = Process.Start(psi);
            if (process is null) return null;

            await process.WaitForExitAsync();
            return File.Exists(thumbnail) ? thumbnail : null;
        }
        catch (Exception ex)
        {
            LogService.Warn($"No se pudo generar la miniatura del video: {ex.Message}");
            return null;
        }
    }
}
