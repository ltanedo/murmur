namespace Murmur;

/// <summary>Tray icon, settings menu and the dictation state machine (the Windows twin of AppDelegate.swift).</summary>
sealed class TrayApp : ApplicationContext
{
    private enum State { Loading, Idle, Starting, Recording, Processing }

    /// <summary>Holding the hotkey longer than this means push-to-talk; a shorter tap toggles.</summary>
    private static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(350);
    /// <summary>How long to keep listening after the hotkey is released, so the last word isn't clipped.</summary>
    private static readonly TimeSpan TrailingAudio = TimeSpan.FromMilliseconds(500);

    private readonly SynchronizationContext _ui;
#if MICROSOFT_SPEECH
    private ISpeechEngine _engine = Settings.UseOnlineSpeech
        ? new OnlineSpeechEngine() : new OnDeviceSpeechEngine();
#else
    private readonly ISpeechEngine _engine = new LocalSpeechEngine(
        Path.Combine(AppContext.BaseDirectory, "Models", "speech-model.bin"));
#endif
    private readonly Overlay _overlay = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusLine;
    private readonly ToolStripMenuItem _engineLine;
    private readonly ToolStripMenuItem _cleanupItem;
    private readonly HotKey _hotKey;
    private readonly bool _darkTaskbar = TrayIcons.IsTaskbarDark();
    private Icon? _icon;

    private State _state = State.Loading;
    private DateTime _pressedAt;
    private bool _toggleMode;
    private bool _stopRequested;
    private bool _cancelRequested;

    private State CurrentState
    {
        get => _state;
        set
        {
            _state = value;
            UpdateIcon();
            switch (value)
            {
                case State.Starting: _overlay.Show(Overlay.Mode.Listening); break;
                case State.Idle or State.Loading: _overlay.Show(Overlay.Mode.Hidden); break;
                // Recording keeps the listening pill; Processing is shown once the trailing audio is in.
            }
        }
    }

    public TrayApp(string[] args)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        _statusLine = new ToolStripMenuItem("Starting…") { Enabled = false };
        _engineLine = new ToolStripMenuItem("Engine: starting…") { Enabled = false };
        _cleanupItem = new ToolStripMenuItem("Clean Up Filler Words") { Checked = Settings.Cleanup, CheckOnClick = true };
        _cleanupItem.CheckedChanged += (_, _) => Settings.Cleanup = _cleanupItem.Checked;

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusLine);
        menu.Items.Add(_engineLine);
        menu.Items.Add(new ToolStripSeparator());
#if MICROSOFT_SPEECH
        var onlineItem = new ToolStripMenuItem("Use Online Dictation (restart required)")
        {
            Checked = Settings.UseOnlineSpeech, CheckOnClick = true,
            ToolTipText = "Uses Microsoft's speech service. Audio may be sent to Microsoft."
        };
        onlineItem.CheckedChanged += (_, _) => Settings.UseOnlineSpeech = onlineItem.Checked;
        menu.Items.Add(onlineItem);
        menu.Items.Add("Speech Settings…", null, (_, _) => OpenSettings("ms-settings:privacy-speech"));
#endif
        menu.Items.Add(_cleanupItem);
        menu.Items.Add("Microphone Settings…", null, (_, _) => OpenSettings("ms-settings:privacy-microphone"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit Murmur", null, (_, _) => ExitThread());

        _tray = new NotifyIcon { Text = "Murmur", ContextMenuStrip = menu, Visible = true };
        UpdateIcon();


        _hotKey = new HotKey();
        _hotKey.OnPress = HotKeyPressed;
        _hotKey.OnRelease = HotKeyReleased;
        _hotKey.OnCancel = CancelDictation;

        if (args.Contains("--demo-overlay")) DemoOverlay();
        _ = PrepareAsync();
    }

    /// <summary>
    /// Loads the edition's engine. Only the Microsoft edition can fall back to online speech.
    /// </summary>
    private async Task PrepareAsync()
    {
        try
        {
#if MICROSOFT_SPEECH
            try { await PrepareEngineAsync(); }
            catch (EngineNotSupportedException e) when (_engine is OnDeviceSpeechEngine)
            {
                _engine.Dispose();
                _engine = new OnlineSpeechEngine();
                await PrepareEngineAsync();
                _tray.ShowBalloonTip(8_000, "Murmur is using online dictation",
                    $"{e.Message} Windows' online dictation is used instead; audio may be sent to Microsoft.",
                    ToolTipIcon.Info);
            }
#else
            await PrepareEngineAsync();
#endif
            _engineLine.Text = $"Engine: {_engine.Label}";
            SetStatus("Hold Right Alt to dictate · tap to toggle");
            CurrentState = State.Idle;
        }
        catch (Exception e)
        {
            Log.Write($"prepare error: {e}");
            _engineLine.Text = "Engine: unavailable";
            SetStatus($"Error: {e.Message}");
            _tray.ShowBalloonTip(10_000, "Murmur can't start dictation", e.Message, ToolTipIcon.Warning);
        }
    }

    private Task PrepareEngineAsync()
    {
        _engine.OnSound = speaking => _ui.Post(_ => _overlay.SetSpeaking(speaking), null);
        return _engine.PrepareAsync(AskUserAsync, SetStatus);
    }

    /// <summary>Yes/no question on the UI thread, used for the model-download consent.</summary>
    private Task<bool> AskUserAsync(string question)
    {
        var tcs = new TaskCompletionSource<bool>();
        _ui.Post(_ =>
        {
            var answer = MessageBox.Show(question, "Murmur", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
            tcs.SetResult(answer == DialogResult.Yes);
        }, null);
        return tcs.Task;
    }

    /// <summary>Shows the overlay with fake activity for a few seconds, for checking how it looks.</summary>
    private void DemoOverlay()
    {
        _overlay.Show(Overlay.Mode.Listening);
        var t = 0.0;
        var timer = new System.Windows.Forms.Timer { Interval = 100 };
        timer.Tick += (_, _) =>
        {
            t += 0.1;
            _overlay.SetSpeaking(Math.Sin(t * 5) > 0);
            if (t > 3) _overlay.Show(Overlay.Mode.Processing);
            if (t > 5) { _overlay.Show(Overlay.Mode.Hidden); timer.Stop(); timer.Dispose(); }
        };
        timer.Start();
    }

    // MARK: Hotkey handling (runs on the UI thread, where the keyboard hook lives)

    private void HotKeyPressed()
    {
        switch (CurrentState)
        {
            case State.Idle:
                _pressedAt = DateTime.UtcNow;
                _toggleMode = false;
                StartDictation();
                break;
            case State.Starting or State.Recording:
                if (_toggleMode) StopDictation();
                break;
            case State.Loading or State.Processing:
                System.Media.SystemSounds.Beep.Play();
                break;
        }
    }

    private void HotKeyReleased()
    {
        if (CurrentState is not (State.Starting or State.Recording) || _toggleMode) return;
        if (DateTime.UtcNow - _pressedAt < HoldThreshold)
            _toggleMode = true; // quick tap: keep listening until the next tap
        else
            StopDictation();
    }

    private async void StartDictation()
    {
        CurrentState = State.Starting;
        _stopRequested = false;
        _cancelRequested = false;
        try
        {
            await _engine.StartAsync();
            CurrentState = State.Recording;
            if (_stopRequested) StopDictation();
        }
        catch (Exception e)
        {
            Log.Write($"start error: {e}");
            SetStatus($"Error: {e.Message}");
            _tray.ShowBalloonTip(5_000, "Murmur", e.Message, ToolTipIcon.Warning);
            CurrentState = State.Idle;
        }
    }

    /// <summary>Right Alt was used for a shortcut, not dictation: throw the recording away.</summary>
    private void CancelDictation()
    {
        if (CurrentState is not (State.Starting or State.Recording) || _toggleMode) return;
        _cancelRequested = true;
        StopDictation();
    }

    private async void StopDictation()
    {
        if (CurrentState != State.Recording)
        {
            _stopRequested = true; // released while the recognizer was still starting up
            return;
        }
        CurrentState = State.Processing;
        try
        {
            string text;
            if (_cancelRequested)
            {
                _overlay.Show(Overlay.Mode.Hidden);
                await _engine.CancelAsync();
                text = "";
            }
            else
            {
                // Keep listening briefly so the last word isn't clipped by letting go of the key.
                await Task.Delay(TrailingAudio);
                _overlay.Show(Overlay.Mode.Processing);
                text = await _engine.StopAsync();
            }
            Log.Write($"transcript: \"{text}\" cancelled={_cancelRequested}");
            if (Settings.Cleanup && text.Length > 0) text = Cleanup.Polish(text);
            if (text.Length > 0 && !TextInserter.Insert(text))
                SetStatus("Couldn't type into that window; the text is on the clipboard (Ctrl+V)");
        }
        catch (Exception e)
        {
            Log.Write($"stop error: {e}");
            SetStatus($"Error: {e.Message}");
        }
        CurrentState = State.Idle;
    }

    // MARK: Tray

    private void UpdateIcon()
    {
        var kind = _state switch
        {
            State.Loading => TrayIcons.Kind.Loading,
            State.Idle => TrayIcons.Kind.Idle,
            State.Starting or State.Recording => TrayIcons.Kind.Recording,
            _ => TrayIcons.Kind.Processing,
        };
        var old = _icon;
        _icon = TrayIcons.Make(kind, _darkTaskbar);
        _tray.Icon = _icon;
        old?.Dispose();
    }

    private void SetStatus(string text)
    {
        _statusLine.Text = text;
        _tray.Text = text.Length > 63 ? "Murmur" : text; // NotifyIcon tooltips are capped at 63 chars
    }

    private static void OpenSettings(string uri)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri) { UseShellExecute = true });
        }
        catch (Exception e)
        {
            Log.Write($"open settings: {e.Message}");
        }
    }

    protected override void ExitThreadCore()
    {
        _tray.Visible = false;
        _hotKey.Dispose();
        _engine.Dispose();
        _overlay.Dispose();
        _tray.Dispose();
        base.ExitThreadCore();
    }
}
