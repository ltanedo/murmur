using System.Runtime.InteropServices;

namespace Murmur;

/// <summary>
/// A hotkey on a single modifier key (Right Alt by default) that reports press and release,
/// so it can be held to talk. If another key is typed while it's held, the user meant a
/// shortcut like Alt+Tab or AltGr+E rather than dictation, so <see cref="OnCancel"/> fires
/// instead of <see cref="OnRelease"/>.
///
/// Uses a low-level keyboard hook, which works system-wide without any special permission.
/// The hook callback runs on the thread that installed it, which must pump messages.
/// </summary>
sealed class HotKey : IDisposable
{
    public Action? OnPress;
    public Action? OnRelease;
    public Action? OnCancel;

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x10;

    private readonly int _vk;
    private readonly LowLevelKeyboardProc _proc; // kept alive so the GC doesn't collect the delegate
    private readonly nint _hook;
    private bool _isDown;
    private bool _usedInShortcut;

    public HotKey(Keys key = Keys.RMenu)
    {
        _vk = (int)key;
        _proc = Callback;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
        if (_hook == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_hook != 0) UnhookWindowsHookEx(_hook);
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            var message = (int)wParam;
            var down = message is WM_KEYDOWN or WM_SYSKEYDOWN;
            var up = message is WM_KEYUP or WM_SYSKEYUP;
            var injected = (info.flags & LLKHF_INJECTED) != 0; // our own SendInput while typing the text

            if ((int)info.vkCode == _vk)
            {
                // Holding a key auto-repeats WM_KEYDOWN; only the first one counts.
                if (down && !_isDown)
                {
                    _isDown = true;
                    _usedInShortcut = false;
                    OnPress?.Invoke();
                }
                else if (up && _isDown)
                {
                    _isDown = false;
                    if (!_usedInShortcut) OnRelease?.Invoke();
                }
            }
            else if (down && _isDown && !injected && !_usedInShortcut && !IsModifier(info.vkCode))
            {
                _usedInShortcut = true;
                OnCancel?.Invoke();
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    /// <summary>On AltGr layouts Right Alt also sends a Left Ctrl; modifiers on their own aren't a shortcut.</summary>
    private static bool IsModifier(uint vk) => vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private delegate nint LowLevelKeyboardProc(int code, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
