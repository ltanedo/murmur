using System.IO;
using System.Text.Json;

namespace Murmur;

/// <summary>User settings, persisted to %APPDATA%\Murmur\settings.json (mirrors the Mac menu).</summary>
public sealed class Settings
{
    public enum Ducking { Off, Lower30, Lower10, Mute }
    public enum Hotkey { RightAlt, RightCtrl, ScrollLock, F13 }

    public Ducking MusicWhileDictating { get; set; } = Ducking.Lower10;
    public bool CleanUpFillerWords { get; set; } = true;
    public bool LaunchAtLogin { get; set; } = true;
    public Hotkey PushToTalkKey { get; set; } = Hotkey.RightAlt;
    public bool DebugLogging { get; set; }

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Murmur");
    private static string FilePath => Path.Combine(Directory, "settings.json");

    public static Settings Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch (Exception e) { Log.Warn("Settings unreadable, using defaults: " + e.Message); }
        return new Settings();
    }

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
