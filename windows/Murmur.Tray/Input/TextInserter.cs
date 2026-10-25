using System.Runtime.InteropServices;
using System.Windows;

namespace Murmur.Input;

/// <summary>
/// Types text into the foreground app by pasting (clipboard saved and restored), with a
/// SendInput Unicode fallback for hosts that do not accept Ctrl+V (console windows).
/// </summary>
public static class TextInserter
{
    public static async Task InsertAsync(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (IsConsoleForeground()) { TypeUnicode(text); return; }
        IDataObject? saved = null;
        try { saved = Clipboard.GetDataObject(); } catch { }
        try
        {
            Clipboard.SetText(text);
            SendKeyChord(VK_CONTROL, 'V');
            await Task.Delay(300); // let the target read the clipboard before restoring
        }
        catch (Exception e)
        {
            Log.Warn("Paste failed, typing instead: " + e.Message);
            TypeUnicode(text);
        }
        finally
        {
            try { if (saved != null) Clipboard.SetDataObject(saved, true); } catch { }
        }
    }

    private static bool IsConsoleForeground()
    {
        var window = GetForegroundWindow();
        var name = new System.Text.StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        string cls = name.ToString();
        return cls == "ConsoleWindowClass" || cls == "CASCADIA_HOSTING_WINDOW_CLASS"; // conhost, Windows Terminal
    }

    private static void SendKeyChord(ushort modifier, char key)
    {
        var inputs = new[]
        {
            Key(modifier, false), Key((ushort)char.ToUpperInvariant(key), false),
            Key((ushort)char.ToUpperInvariant(key), true), Key(modifier, true),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void TypeUnicode(string text)
    {
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (char c in text)
        {
            inputs.Add(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } } });
            inputs.Add(new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } });
        }
        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static INPUT Key(ushort vk, bool up) =>
        new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } } };

    private const ushort VK_CONTROL = 0x11;
    private const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004;
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
}
