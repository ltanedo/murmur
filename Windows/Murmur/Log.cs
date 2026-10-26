namespace Murmur;

/// <summary>Debug logging to %LOCALAPPDATA%\Murmur\Murmur.log. Off by default: the log contains transcripts.</summary>
static class Log
{
    public static bool Enabled { get; set; }

    public static readonly string Path =
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Murmur", "Murmur.log");

    private static readonly object Gate = new();

    public static void Write(string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
