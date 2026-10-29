using Microsoft.Win32;

namespace Murmur;

/// <summary>User settings, stored under HKCU\Software\Murmur (the Windows equivalent of UserDefaults).</summary>
static class Settings
{
    private const string KeyPath = @"Software\Murmur";

    public static bool UseOnlineSpeech
    {
        get => ReadBool("useOnlineSpeech", true);
        set => Write("useOnlineSpeech", value);
    }

    public static bool Cleanup
    {
        get => ReadBool("cleanup", true);
        set => Write("cleanup", value);
    }

    public static bool DebugLogging
    {
        get => ReadBool("debugLogging", false);
        set => Write("debugLogging", value);
    }

    private static bool ReadBool(string name, bool fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(name) is int value ? value != 0 : fallback;
    }

    private static void Write(string name, bool value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(name, value ? 1 : 0, RegistryValueKind.DWord);
    }
}
