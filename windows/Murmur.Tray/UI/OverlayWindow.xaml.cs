using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Murmur.UI;

/// <summary>
/// The floating pill at the bottom of the active monitor: level meter while
/// listening, streamed words, pulsing dots while finishing. Click-through and never
/// activated, so focus stays in the app being dictated into.
/// </summary>
public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer pulse = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int dots;
    private bool handsFree;

    public OverlayWindow()
    {
        InitializeComponent();
        pulse.Tick += (_, _) => { dots = (dots + 1) % 4; Caption.Text = "Transcribing" + new string('.', dots); };
        SourceInitialized += (_, _) => MakeClickThrough();
    }

    public void ShowListening()
    {
        handsFree = false; Caption.Text = "Listening…"; Meter.Visibility = Visibility.Visible; pulse.Stop();
        Place(); Show();
    }

    public void SetHandsFree(bool on) { handsFree = on; if (on) Caption.Text = "Listening (tap again to stop)"; }
    public void ShowText(string text) { if (!string.IsNullOrWhiteSpace(text)) Caption.Text = text; }
    public void ShowFinishing() { Meter.Visibility = Visibility.Collapsed; dots = 0; Caption.Text = "Transcribing"; pulse.Start(); }
    public new void Hide() { pulse.Stop(); base.Hide(); }

    public void SetLevel(float level)
    {
        float l = Math.Clamp(level * 3f, 0f, 1f);
        Bar1.Height = 6 + 10 * l; Bar2.Height = 8 + 16 * l; Bar3.Height = 10 + 22 * l; Bar4.Height = 8 + 16 * l; Bar5.Height = 6 + 10 * l;
    }

    private void Place()
    {
        // Bottom centre of the monitor that holds the mouse cursor (closest to "active").
        var area = SystemParameters.WorkArea; // TODO (Windows agent): use the cursor's monitor via MonitorFromPoint for multi-monitor setups
        Left = area.Left + (area.Width - Width) / 2; Top = area.Bottom - Height - 24;
    }

    private void MakeClickThrough()
    {
        var handle = new WindowInteropHelper(this).Handle;
        int style = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, style | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOOLWINDOW = 0x80;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
