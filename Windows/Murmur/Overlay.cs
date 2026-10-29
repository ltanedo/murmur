using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Murmur;

/// <summary>
/// The small floating pill at the bottom of the screen: a red dot while listening (bigger while
/// sound is heard) and pulsing dots while transcribing. It never takes focus and clicks go
/// through it, so the text still lands in the window the user was typing in.
/// </summary>
sealed class Overlay : Form
{
    public enum Mode { Hidden, Listening, Processing }

    private Mode _mode = Mode.Hidden;
    private bool _speaking;
    private int _tick;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 80 };

    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int SW_SHOWNOACTIVATE = 4;

    public Overlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        DoubleBuffered = true;
        Size = new Size(160, 40);
        _timer.Tick += (_, _) => { _tick++; Invalidate(); };
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_TRANSPARENT;
            return cp;
        }
    }

    public void Show(Mode mode)
    {
        _mode = mode;
        if (mode == Mode.Hidden)
        {
            _timer.Stop();
            _speaking = false;
            Hide();
            return;
        }
        var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(screen.Left + (screen.Width - Width) / 2, screen.Bottom - Height - 24);
        if (!Visible) ShowWindow(Handle, SW_SHOWNOACTIVATE);
        _timer.Start();
        Invalidate();
    }

    public void SetSpeaking(bool speaking)
    {
        _speaking = speaking;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Height / 2);
        using var fill = new SolidBrush(Color.FromArgb(235, 30, 30, 34));
        g.FillPath(fill, path);

        var text = _mode == Mode.Listening ? "Listening" : "Transcribing";
        using var font = new Font("Segoe UI", 10f, FontStyle.Regular);
        var textSize = g.MeasureString(text, font);
        var contentWidth = 22 + textSize.Width;
        var x = (Width - contentWidth) / 2;
        var cy = Height / 2f;

        if (_mode == Mode.Listening)
        {
            // A red dot that grows while the recognizer hears sound, and breathes while waiting.
            var pulse = _speaking ? 1f : 0.55f + 0.2f * MathF.Sin(_tick * 0.35f);
            var radius = 6f * pulse + 1f;
            using var brush = new SolidBrush(Color.FromArgb(255, 235, 70, 70));
            g.FillEllipse(brush, x + 7 - radius, cy - radius, radius * 2, radius * 2);
        }
        else
        {
            using var brush = new SolidBrush(Color.FromArgb(255, 200, 200, 205));
            for (var i = 0; i < 3; i++)
            {
                var phase = (_tick / 3 + i) % 3;
                var radius = phase == 0 ? 3f : 2f;
                g.FillEllipse(brush, x + 2 + i * 5 - radius, cy - radius, radius * 2, radius * 2);
            }
        }

        using var textBrush = new SolidBrush(Color.White);
        g.DrawString(text, font, textBrush, x + 22, cy - textSize.Height / 2);
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);
}
