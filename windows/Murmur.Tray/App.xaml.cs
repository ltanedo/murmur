using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Microsoft.Win32;
using Murmur.Audio;
using Murmur.Hotkey;
using Murmur.Input;
using Murmur.Speech;
using Murmur.UI;

namespace Murmur;

/// <summary>
/// Tray icon, settings menu and the dictation state machine (mirrors AppDelegate.swift):
/// idle -> listening (key held or hands-free) -> finishing -> idle.
/// </summary>
public partial class App : Application
{
    private enum State { Idle, Listening, HandsFree, Finishing }

    private static Mutex? single;
    private Settings settings = new();
    private TaskbarIcon? tray;
    private KeyboardHook? hook;
    private Dictation? dictation;
    private Capture? capture;
    private readonly Ducker ducker = new();
    private OverlayWindow? overlay;
    private State state = State.Idle;
    private DateTime pressedAt;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        single = new Mutex(true, "app.murmur.dictation.windows", out bool first);
        if (!first) { Shutdown(); return; }
        settings = Settings.Load();
        Log.Debug = settings.DebugLogging;
        Log.Info("Murmur starting");
        overlay = new OverlayWindow();
        BuildTray();
        ApplyLaunchAtLogin();
        dictation = new Dictation();
        dictation.Partial += text => overlay?.ShowText(text);
        dictation.Completed += OnCompleted;
        dictation.Failed += message => { overlay?.Hide(); Notify(message); OfferSpeechSettings(message); };
        await dictation.WarmUpAsync();
        InstallHook();
    }

    private void InstallHook()
    {
        hook?.Dispose();
        hook = new KeyboardHook(settings.PushToTalkKey, Dispatcher);
        hook.Pressed += OnKeyDown;
        hook.Released += OnKeyUp;
        hook.Chord += () => _ = CancelAsync();
    }

    private void OnKeyDown()
    {
        pressedAt = DateTime.UtcNow;
        if (state == State.HandsFree) { _ = FinishAsync(); return; } // tap again stops hands-free
        if (state != State.Idle) return;
        _ = BeginAsync(State.Listening);
    }

    private void OnKeyUp()
    {
        if (state != State.Listening) return;
        // A quick tap switches to hands-free; a hold ends on release.
        if ((DateTime.UtcNow - pressedAt).TotalMilliseconds < 250) { state = State.HandsFree; overlay?.SetHandsFree(true); return; }
        _ = FinishAsync();
    }

    private async Task BeginAsync(State next)
    {
        state = next;
        ducker.Duck(settings.MusicWhileDictating);
        capture = new Capture();
        capture.Level += level => Dispatcher.BeginInvoke(() => overlay?.SetLevel(level));
        capture.Start();
        overlay?.ShowListening();
        await dictation!.StartAsync();
    }

    private async Task FinishAsync()
    {
        if (state is not (State.Listening or State.HandsFree)) return;
        state = State.Finishing;
        overlay?.ShowFinishing();
        var tail = capture?.StopAsync(500);
        await dictation!.StopAsync();
        if (tail != null) await tail;
        capture?.Dispose(); capture = null;
    }

    private async void OnCompleted(string text)
    {
        try
        {
            if (settings.CleanUpFillerWords) text = Cleanup.Apply(text);
            Log.Transcript(text);
            if (!string.IsNullOrWhiteSpace(text)) await TextInserter.InsertAsync(text);
        }
        finally
        {
            overlay?.Hide();
            await ducker.RestoreAsync();
            state = State.Idle;
        }
    }

    private async Task CancelAsync()
    {
        if (state == State.Idle) return;
        state = State.Idle;
        overlay?.Hide();
        capture?.Dispose(); capture = null;
        await dictation!.CancelAsync();
        await ducker.RestoreAsync();
    }

    private void BuildTray()
    {
        tray = new TaskbarIcon { ToolTipText = "Murmur: hold " + settings.PushToTalkKey + " to dictate" };
        try { tray.Icon = new System.Drawing.Icon(Path.Combine(AppContext.BaseDirectory, "murmur.ico")); } catch { }
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Murmur " + typeof(App).Assembly.GetName().Version, IsEnabled = false });
        menu.Items.Add(new Separator());
        var music = new MenuItem { Header = "Music While Dictating" };
        foreach (var mode in Enum.GetValues<Settings.Ducking>())
        {
            var item = new MenuItem { Header = mode switch { Settings.Ducking.Off => "Off", Settings.Ducking.Lower30 => "Lower to 30%", Settings.Ducking.Lower10 => "Lower to 10%", _ => "Mute" }, IsCheckable = true, IsChecked = settings.MusicWhileDictating == mode };
            item.Click += (_, _) => { settings.MusicWhileDictating = mode; settings.Save(); foreach (MenuItem m in music.Items) m.IsChecked = m == item; };
            music.Items.Add(item);
        }
        menu.Items.Add(music);
        var cleanup = new MenuItem { Header = "Clean Up Filler Words", IsCheckable = true, IsChecked = settings.CleanUpFillerWords };
        cleanup.Click += (_, _) => { settings.CleanUpFillerWords = cleanup.IsChecked; settings.Save(); };
        menu.Items.Add(cleanup);
        var hotkey = new MenuItem { Header = "Hotkey" };
        foreach (var key in Enum.GetValues<Settings.Hotkey>())
        {
            var item = new MenuItem { Header = key.ToString(), IsCheckable = true, IsChecked = settings.PushToTalkKey == key };
            item.Click += (_, _) => { settings.PushToTalkKey = key; settings.Save(); foreach (MenuItem m in hotkey.Items) m.IsChecked = m == item; InstallHook(); };
            hotkey.Items.Add(item);
        }
        menu.Items.Add(hotkey);
        var login = new MenuItem { Header = "Launch at Login", IsCheckable = true, IsChecked = settings.LaunchAtLogin };
        login.Click += (_, _) => { settings.LaunchAtLogin = login.IsChecked; settings.Save(); ApplyLaunchAtLogin(); };
        menu.Items.Add(login);
        menu.Items.Add(new Separator());
        var privacy = new MenuItem { Header = "Open Speech Privacy Settings…" };
        privacy.Click += (_, _) => OpenSettings("ms-settings:privacy-speech");
        menu.Items.Add(privacy);
        var mic = new MenuItem { Header = "Open Microphone Settings…" };
        mic.Click += (_, _) => OpenSettings("ms-settings:privacy-microphone");
        menu.Items.Add(mic);
        menu.Items.Add(new Separator());
        var quit = new MenuItem { Header = "Quit Murmur" };
        quit.Click += (_, _) => Shutdown();
        menu.Items.Add(quit);
        tray.ContextMenu = menu;
        tray.ForceCreate();
    }

    private static void OpenSettings(string uri) { try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch { } }

    private void OfferSpeechSettings(string message)
    {
        if (!message.Contains("Online speech recognition")) return;
        var answer = MessageBox.Show(message + "\n\nOpen the setting now?", "Murmur", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer == MessageBoxResult.Yes) OpenSettings("ms-settings:privacy-speech");
    }

    private void Notify(string message) { try { tray?.ShowNotification("Murmur", message); } catch { } }

    private void ApplyLaunchAtLogin()
    {
        try
        {
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run == null) return;
            if (settings.LaunchAtLogin) run.SetValue("Murmur", "\"" + Environment.ProcessPath + "\"");
            else run.DeleteValue("Murmur", throwOnMissingValue: false);
        }
        catch (Exception e) { Log.Warn("Launch at Login failed: " + e.Message); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        hook?.Dispose(); dictation?.Dispose(); capture?.Dispose(); tray?.Dispose();
        base.OnExit(e);
    }
}
