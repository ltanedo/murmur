using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Murmur.Hotkey;

/// <summary>
/// Low-level keyboard hook implementing the Mac app's Right-Option semantics on a
/// chosen key: hold = push-to-talk, tap = hands-free toggle, key + any other key =
/// normal shortcut (dictation cancelled, nothing swallowed).
/// </summary>
public sealed class KeyboardHook : IDisposable
{
    public event Action? Pressed;      // key went down alone
    public event Action? Released;     // key came up (after Pressed) without another key
    public event Action? Chord;        // another key was pressed while held: cancel

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    private const int VK_RMENU = 0xA5, VK_RCONTROL = 0xA3, VK_SCROLL = 0x91, VK_F13 = 0x7C;

    private readonly int virtualKey;
    private readonly Dispatcher dispatcher;
    private readonly LowLevelKeyboardProc callback;
    private IntPtr hook;
    private bool held, chorded;

    public KeyboardHook(Settings.Hotkey key, Dispatcher dispatcher)
    {
        virtualKey = key switch
        {
            Settings.Hotkey.RightCtrl => VK_RCONTROL,
            Settings.Hotkey.ScrollLock => VK_SCROLL,
            Settings.Hotkey.F13 => VK_F13,
            _ => VK_RMENU,
        };
        this.dispatcher = dispatcher;
        callback = HookProc; // keep a reference so the delegate is not collected
        using var module = Process.GetCurrentProcess().MainModule!;
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, callback, GetModuleHandle(module.ModuleName), 0);
        if (hook == IntPtr.Zero) Log.Error("Keyboard hook failed: " + Marshal.GetLastWin32Error());
    }

    private IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int message = (int)wParam;
            int vk = Marshal.ReadInt32(lParam);
            bool down = message is WM_KEYDOWN or WM_SYSKEYDOWN, up = message is WM_KEYUP or WM_SYSKEYUP;
            if (vk == virtualKey)
            {
                if (down && !held) { held = true; chorded = false; dispatcher.BeginInvoke(() => Pressed?.Invoke()); }
                else if (up && held)
                {
                    held = false;
                    bool wasChord = chorded;
                    dispatcher.BeginInvoke(() => { if (!wasChord) Released?.Invoke(); });
                }
            }
            else if (down && held && !chorded)
            {
                // The user is using the key as a modifier: cancel and stay out of the way.
                chorded = true;
                dispatcher.BeginInvoke(() => Chord?.Invoke());
            }
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    public void Dispose()
    {
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
