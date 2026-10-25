using Windows.Globalization;
using Windows.Media.SpeechRecognition;

namespace Murmur.Speech;

/// <summary>
/// Microsoft's free cloud dictation through Windows.Media.SpeechRecognition, driven
/// like a walkie-talkie: Start on key-down, Stop on key-up. The engine ends a
/// continuous session on its own after a silence timeout; while the key is still
/// held we keep the text so far and start another session immediately (the same
/// pattern unified-unity uses for Android), joining segments with spaces.
///
/// Requires Windows "Online speech recognition" (ms-settings:privacy-speech).
/// TODO (Windows agent): verify this works from this unpackaged WPF app on Windows 10
/// 22H2 and 11; if package identity turns out to be required, switch the installer to
/// a sparse MSIX package per the spec rather than dropping the feature.
/// </summary>
public sealed class Dictation : IDisposable
{
    public event Action<string>? Partial;   // accumulated text + current hypothesis
    public event Action<string>? Completed; // final joined transcript after Stop
    public event Action<string>? Failed;    // user-facing message

    private SpeechRecognizer? recognizer;
    private readonly List<string> segments = new();
    private string hypothesis = string.Empty;
    private bool listening, stopping, ready;
    private TaskCompletionSource<bool>? sessionEnded;

    public bool IsReady => ready;

    /// <summary>Create and compile ahead of time so the first hold starts instantly.</summary>
    public async Task WarmUpAsync()
    {
        try
        {
            recognizer?.Dispose();
            recognizer = new SpeechRecognizer(SpeechRecognizer.SystemSpeechLanguage);
            recognizer.Constraints.Add(new SpeechRecognitionTopicConstraint(SpeechRecognitionScenario.Dictation, "dictation"));
            // Longest windows the API accepts; the resume loop covers the rest.
            recognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(60);
            recognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(10);
            recognizer.ContinuousRecognitionSession.AutoStopSilenceTimeout = TimeSpan.FromSeconds(60);
            var compiled = await recognizer.CompileConstraintsAsync();
            if (compiled.Status != SpeechRecognitionResultStatus.Success)
            {
                ready = false;
                Failed?.Invoke(Describe(compiled.Status));
                return;
            }
            recognizer.HypothesisGenerated += (_, e) => { hypothesis = e.Hypothesis.Text; Partial?.Invoke(Joined(hypothesis)); };
            recognizer.ContinuousRecognitionSession.ResultGenerated += (_, e) =>
            {
                if (e.Result.Status != SpeechRecognitionResultStatus.Success || string.IsNullOrWhiteSpace(e.Result.Text)) return;
                segments.Add(e.Result.Text.Trim()); hypothesis = string.Empty;
                Partial?.Invoke(Joined(string.Empty));
            };
            recognizer.ContinuousRecognitionSession.Completed += async (_, e) =>
            {
                Log.Info("Session completed: " + e.Status);
                if (listening && !stopping && e.Status is SpeechRecognitionResultStatus.TimeoutExceeded or SpeechRecognitionResultStatus.Success)
                {
                    try { await recognizer!.ContinuousRecognitionSession.StartAsync(); return; } // keep going while held
                    catch (Exception ex) { Log.Warn("Resume failed: " + ex.Message); }
                }
                sessionEnded?.TrySetResult(true);
            };
            ready = true;
            Log.Info("Dictation ready for " + recognizer.CurrentLanguage.LanguageTag);
        }
        catch (Exception e)
        {
            ready = false;
            Log.Error("Dictation warm-up failed: " + e.Message);
            Failed?.Invoke("Speech recognition is unavailable. Turn on Online speech recognition in Settings > Privacy & security > Speech.");
        }
    }

    public async Task StartAsync()
    {
        if (!ready || recognizer == null) { await WarmUpAsync(); if (!ready) return; }
        if (listening) return;
        segments.Clear(); hypothesis = string.Empty; stopping = false; listening = true;
        sessionEnded = new TaskCompletionSource<bool>();
        try { await recognizer!.ContinuousRecognitionSession.StartAsync(); }
        catch (Exception e)
        {
            listening = false;
            Log.Error("Start failed: " + e.Message);
            Failed?.Invoke("Could not start listening. Check microphone access and Online speech recognition in Settings.");
        }
    }

    /// <summary>Stop (not cancel) so the final segment flushes, wait briefly, then deliver.</summary>
    public async Task StopAsync()
    {
        if (!listening || recognizer == null) return;
        stopping = true;
        try { await recognizer.ContinuousRecognitionSession.StopAsync(); } catch (Exception e) { Log.Warn("Stop failed: " + e.Message); }
        if (sessionEnded != null) await Task.WhenAny(sessionEnded.Task, Task.Delay(3000));
        listening = false;
        Completed?.Invoke(Joined(hypothesis));
    }

    public async Task CancelAsync()
    {
        if (!listening || recognizer == null) return;
        stopping = true; listening = false;
        try { await recognizer.ContinuousRecognitionSession.CancelAsync(); } catch { }
        segments.Clear(); hypothesis = string.Empty;
    }

    private string Joined(string tail)
    {
        string head = string.Join(' ', segments);
        tail = tail.Trim();
        return head.Length == 0 ? tail : tail.Length == 0 ? head : head + " " + tail;
    }

    private static string Describe(SpeechRecognitionResultStatus status) => status switch
    {
        SpeechRecognitionResultStatus.TopicLanguageNotSupported => "Dictation is not available for your Windows speech language.",
        SpeechRecognitionResultStatus.NetworkFailure => "Speech recognition needs a network connection.",
        SpeechRecognitionResultStatus.MicrophoneUnavailable => "No microphone is available.",
        SpeechRecognitionResultStatus.UserCanceled => "Speech recognition was cancelled.",
        _ => "Speech recognition failed (" + status + "). Check Online speech recognition in Settings > Privacy & security > Speech.",
    };

    public void Dispose() { recognizer?.Dispose(); recognizer = null; ready = false; }
}
