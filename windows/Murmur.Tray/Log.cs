using System.IO;

namespace Murmur;

/// <summary>Rolling log under %LOCALAPPDATA%\Murmur\Logs. Never logs transcript text unless DebugLogging is on.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "Logs");
    private static readonly string File = Path.Combine(Dir, "murmur.log");
    public static bool Debug { get; set; }

    public static void Info(string message) => Write("I", message);
    public static void Warn(string message) => Write("W", message);
    public static void Error(string message) => Write("E", message);
    /// <summary>Transcript-bearing lines go through here and are dropped unless debugging.</summary>
    public static void Transcript(string message) { if (Debug) Write("T", message); }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Dir);
                if (System.IO.File.Exists(File) && new FileInfo(File).Length > 2_000_000)
                    System.IO.File.Move(File, Path.Combine(Dir, "murmur.1.log"), overwrite: true);
                System.IO.File.AppendAllText(File, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
        }
        catch { /* logging must never crash dictation */ }
    }
}
