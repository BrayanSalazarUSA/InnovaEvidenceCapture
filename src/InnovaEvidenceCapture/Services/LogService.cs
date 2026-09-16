using System.IO;

namespace InnovaEvidenceCapture.Services;

/// <summary>
/// Log diario en disco. Con el programa en 30 PCs, cuando un agente dice "no me
/// subio", lo primero que se pide es este archivo.
/// </summary>
public static class LogService
{
    private static readonly object Gate = new();
    private static string _folder = Path.Combine(@"C:\InnovaEvidence", "logs");

    public static void Configure(string captureRoot)
    {
        _folder = Path.Combine(captureRoot, "logs");
        try { Directory.CreateDirectory(_folder); } catch { /* sin log no se cae la app */ }
        Purge();
    }

    public static string Folder => _folder;

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} :: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(_folder);
                File.AppendAllText(
                    Path.Combine(_folder, $"evidence-capture-{DateTime.Now:yyyy-MM-dd}.log"),
                    line + Environment.NewLine);
            }
        }
        catch
        {
            // Si no se puede escribir el log, se sigue trabajando igual.
        }

        System.Diagnostics.Debug.WriteLine(line);
    }

    /// <summary>Conserva 30 dias de log; mas que eso no le sirve a nadie.</summary>
    private static void Purge()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-30);
            foreach (var file in Directory.EnumerateFiles(_folder, "evidence-capture-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff) File.Delete(file);
            }
        }
        catch { /* limpieza best-effort */ }
    }
}
