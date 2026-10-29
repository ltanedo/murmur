using System.Drawing.Drawing2D;

namespace Murmur;

/// <summary>Draws the tray microphone at runtime, so no icon files need shipping.</summary>
static class TrayIcons
{
    public enum Kind { Loading, Idle, Recording, Processing }

    public static Icon Make(Kind kind, bool darkTaskbar)
    {
        const int size = 32;
        using var bitmap = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            var color = kind switch
            {
                Kind.Recording => Color.FromArgb(255, 235, 70, 70),
                Kind.Loading => darkTaskbar ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(150, 0, 0, 0),
                _ => darkTaskbar ? Color.White : Color.Black,
            };
            using var pen = new Pen(color, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            using var brush = new SolidBrush(color);

            // Capsule
            using var capsule = Capsule(new RectangleF(11, 3, 10, 16));
            if (kind is Kind.Recording or Kind.Processing)
                g.FillPath(brush, capsule);
            else
                g.DrawPath(pen, capsule);
            // Cradle, stem and base
            g.DrawArc(pen, new RectangleF(7, 8, 18, 16), 0, 180);
            g.DrawLine(pen, 16, 24, 16, 28);
            g.DrawLine(pen, 11, 28.5f, 21, 28.5f);

            if (kind == Kind.Processing)
            {
                using var dot = new SolidBrush(darkTaskbar ? Color.FromArgb(255, 30, 30, 34) : Color.White);
                for (var i = 0; i < 3; i++) g.FillEllipse(dot, 13.2f + i * 2.6f, 10.2f, 1.6f, 1.6f);
            }
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }

    private static GraphicsPath Capsule(RectangleF r)
    {
        var path = new GraphicsPath();
        var d = r.Width;
        path.AddArc(r.X, r.Y, d, d, 180, 180);
        path.AddArc(r.X, r.Bottom - d, d, d, 0, 180);
        path.CloseFigure();
        return path;
    }

    public static bool IsTaskbarDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is not int light || light == 0;
        }
        catch
        {
            return true;
        }
    }
}
