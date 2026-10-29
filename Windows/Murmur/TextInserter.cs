using System.Runtime.InteropServices;

namespace Murmur;

/// <summary>
/// Types text into whatever window is focused. Characters are sent as Unicode key events with
/// SendInput, so the clipboard is never touched and any keyboard layout works. If Windows refuses
/// (for example, the focused window is elevated and Murmur is not), the text is put on the
/// clipboard instead so it can be pasted by hand.
/// </summary>
static class TextInserter
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const ushort VK_RETURN = 0x0D;

    /// <summary>Returns false if typing wasn't possible; the text is then on the clipboard.</summary>
    public static bool Insert(string text)
    {
        if (text.Length == 0) return true;
        var inputs = new List<INPUT>(text.Length * 2);
        foreach (var ch in text)
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                inputs.Add(Key(VK_RETURN, 0, 0));
                inputs.Add(Key(VK_RETURN, 0, KEYEVENTF_KEYUP));
                continue;
            }
            // Surrogate pairs (emoji etc.) go as two UTF-16 units, which Windows reassembles.
            inputs.Add(Key(0, ch, KEYEVENTF_UNICODE));
            inputs.Add(Key(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }

        var array = inputs.ToArray();
        var sent = SendInput((uint)array.Length, array, Marshal.SizeOf<INPUT>());
        if (sent == array.Length) return true;

        Log.Write($"SendInput sent {sent}/{array.Length}, error {Marshal.GetLastWin32Error()}; falling back to clipboard");
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception e)
        {
            Log.Write($"clipboard error: {e.Message}");
        }
        return false;
    }

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
