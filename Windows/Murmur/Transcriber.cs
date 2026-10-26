using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace Murmur;

sealed class TranscriberException(string message) : Exception(message);

/// <summary>
/// Captures the microphone and transcribes it with the in-box Windows speech recognizer
/// (Windows.Media.SpeechRecognition). The recognizer owns the microphone itself: a continuous
/// recognition session is started when the hotkey goes down and stopped when it comes up, and
/// every phrase it produced in between is joined into the transcript.
///
/// Recognition uses the speech language pack installed for the Windows speech language.
/// No account, key or cloud resource is configured; this is the same engine as Windows' own
/// dictation.
/// </summary>
sealed class Transcriber : IDisposable
{
    private SpeechRecognizer? _recognizer;
    private readonly List<string> _phrases = [];
    private readonly object _gate = new();
    private TaskCompletionSource? _completed;
    private bool _listening;

    public Language? Language { get; private set; }

    /// <summary>Fires when the recognizer hears sound start or stop, for the on-screen indicator.</summary>
    public Action<bool>? OnSound;

    /// <summary>Creates the recognizer for the Windows speech language and compiles the dictation grammar.</summary>
    public async Task PrepareAsync()
    {
        var language = SpeechRecognizer.SystemSpeechLanguage;
        var supported = SpeechRecognizer.SupportedTopicLanguages;
        Log.Write($"speech language {language?.LanguageTag}, dictation languages: " +
                  string.Join(", ", supported.Select(l => l.LanguageTag)));
        if (language is null || !supported.Any(l => l.LanguageTag.Equals(language.LanguageTag, StringComparison.OrdinalIgnoreCase)))
        {
            var name = language?.DisplayName ?? "your language";
            throw new TranscriberException(
                $"No dictation support for {name}. Install its speech language pack in Settings > Time & language > Language & region.");
        }
        Language = language;

        var recognizer = new SpeechRecognizer(language);
        recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
        // The hotkey decides when we stop, not silence.
        recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromMinutes(2);
        recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(1);
        recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromMinutes(2);

        var compiled = await recognizer.CompileConstraintsAsync();
        if (compiled.Status != SpeechRecognitionResultStatus.Success)
        {
            recognizer.Dispose();
            throw new TranscriberException(DescribeFailure(compiled.Status));
        }

        recognizer.ContinuousRecognitionSession.ResultGenerated += OnResult;
        recognizer.ContinuousRecognitionSession.Completed += OnCompleted;
        recognizer.StateChanged += OnStateChanged;
        _recognizer = recognizer;
        Log.Write($"dictation grammar compiled for {language.LanguageTag}");
    }

    /// <summary>Starts listening. Returns once the microphone is open.</summary>
    public async Task StartAsync()
    {
        var recognizer = _recognizer ?? throw new TranscriberException("The speech recognizer isn't ready yet.");
        lock (_gate)
        {
            _phrases.Clear();
            _completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _listening = true;
        }
        try
        {
            await recognizer.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception e)
        {
            lock (_gate) _listening = false;
            throw new TranscriberException(DescribeStartFailure(e));
        }
    }

    /// <summary>Stops listening, waits for the recognizer to finish the audio it has, and returns the transcript.</summary>
    public async Task<string> StopAsync()
    {
        var recognizer = _recognizer;
        TaskCompletionSource? completed;
        bool listening;
        lock (_gate)
        {
            completed = _completed;
            listening = _listening;
            _listening = false;
        }
        if (recognizer is null || completed is null) return "";
        if (listening)
        {
            try
            {
                await recognizer.ContinuousRecognitionSession.StopAsync();
            }
            catch (Exception e)
            {
                // The session may already have ended on its own (e.g. a timeout); Completed has or will fire.
                Log.Write($"StopAsync: {e.Message}");
            }
        }
        // Completed is the point after which no more results will be generated.
        await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        lock (_gate)
        {
            var text = string.Join(" ", _phrases.Select(p => p.Trim()).Where(p => p.Length > 0)).Trim();
            _phrases.Clear();
            return text;
        }
    }

    /// <summary>Stops listening and throws the audio away.</summary>
    public async Task CancelAsync()
    {
        var recognizer = _recognizer;
        bool listening;
        lock (_gate)
        {
            listening = _listening;
            _listening = false;
            _phrases.Clear();
        }
        if (recognizer is null || !listening) return;
        try
        {
            await recognizer.ContinuousRecognitionSession.CancelAsync();
        }
        catch (Exception e)
        {
            Log.Write($"CancelAsync: {e.Message}");
        }
    }

    private void OnResult(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        var result = args.Result;
        Log.Write($"result {result.Status} confidence={result.Confidence}: \"{result.Text}\"");
        if (result.Status != SpeechRecognitionResultStatus.Success || result.Confidence == SpeechRecognitionConfidence.Rejected) return;
        lock (_gate) _phrases.Add(result.Text);
    }

    private void OnCompleted(SpeechContinuousRecognitionSession sender, SpeechContinuousRecognitionCompletedEventArgs args)
    {
        Log.Write($"session completed: {args.Status}");
        TaskCompletionSource? completed;
        lock (_gate)
        {
            _listening = false;
            completed = _completed;
        }
        completed?.TrySetResult();
    }

    private void OnStateChanged(SpeechRecognizer sender, SpeechRecognizerStateChangedEventArgs args)
    {
        Log.Write($"recognizer state: {args.State}");
        switch (args.State)
        {
            case SpeechRecognizerState.SoundStarted:
            case SpeechRecognizerState.SpeechDetected:
                OnSound?.Invoke(true);
                break;
            case SpeechRecognizerState.SoundEnded:
            case SpeechRecognizerState.Idle:
            case SpeechRecognizerState.Paused:
                OnSound?.Invoke(false);
                break;
        }
    }

    private static string DescribeFailure(SpeechRecognitionResultStatus status) => status switch
    {
        SpeechRecognitionResultStatus.MicrophoneUnavailable =>
            "No microphone. Check Settings > Privacy & security > Microphone (allow desktop apps).",
        SpeechRecognitionResultStatus.UserCanceled => "Dictation was cancelled.",
        _ => $"Speech recognition failed ({status}).",
    };

    private static string DescribeStartFailure(Exception e)
    {
        // 0x80045509: the Windows speech privacy setting is off.
        if ((uint)e.HResult == 0x80045509 || e.Message.Contains("privacy", StringComparison.OrdinalIgnoreCase))
            return "Turn on \"Online speech recognition\" in Settings > Privacy & security > Speech, then try again.";
        if ((uint)e.HResult == 0x80070005)
            return "Microphone access is blocked. Allow desktop apps in Settings > Privacy & security > Microphone.";
        return $"Couldn't start listening: {e.Message}";
    }

    public void Dispose()
    {
        var recognizer = _recognizer;
        _recognizer = null;
        if (recognizer is null) return;
        recognizer.ContinuousRecognitionSession.ResultGenerated -= OnResult;
        recognizer.ContinuousRecognitionSession.Completed -= OnCompleted;
        recognizer.StateChanged -= OnStateChanged;
        recognizer.Dispose();
    }
}
