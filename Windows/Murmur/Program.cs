using System.Runtime.InteropServices;

namespace Murmur;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // A second copy would fight over the hotkey, so only ever run one.
        using var instance = new Mutex(initiallyOwned: true, name: @"Local\app.murmur.dictation", out var isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Murmur is already running. Look for the microphone in the tray.", "Murmur",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Log.Enabled = args.Contains("--debug") || Settings.DebugLogging;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(args));
    }
}
