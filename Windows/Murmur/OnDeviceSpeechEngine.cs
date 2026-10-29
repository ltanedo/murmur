using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Speech;
using Windows.Media.Devices;

namespace Murmur;

/// <summary>
/// On-device transcription with the Windows AI Speech API (Microsoft.Windows.AI.Speech).
/// The model runs on the NPU of a Copilot+ PC or on the CPU; audio never leaves the machine
/// and there is no key or account. The API is experimental: the app has to be MSIX-packaged
/// with the systemAIModels capability, and the model is downloaded through Windows Update
/// the first time it's needed on a CPU-only PC.
///
/// A StreamingRecognition is created when the hotkey goes down and stopped when it comes up.
/// Every final phrase it raised in between, plus whatever it was still recognizing at the
/// moment of release, becomes the transcript.
/// </summary>
sealed class OnDeviceSpeechEngine : ISpeechEngine
{
    public string Label => "On-device";
    public bool IsOnDevice => true;
    public Action<bool>? OnSound { get; set; }
    public Action? OnCaptureStopped { get; set; }

    private SpeechRecognitionModel? _model;
    private StreamingRecognition? _recognition;
    private readonly object _gate = new();
    private readonly List<string> _phrases = [];
    private string _partial = "";
    private TaskCompletionSource? _flushed;

    public async Task PrepareAsync(Func<string, Task<bool>> askUser, Action<string> status)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100))
            throw new EngineNotSupportedException("Windows AI speech requires Windows 11 24H2 (build 26100) or later.");
        SpeechRecognitionModelFactory factory;
        AIFeatureReadyState state;
        try
        {
            factory = SpeechRecognitionModelFactory.Default;
            state = factory.GetReadyState();
        }
        catch (Exception e)
        {
            // Typically: running unpackaged (REGDB_E_CLASSNOTREG), the systemAIModels capability is
            // missing (UnauthorizedAccessException), or the Windows App Runtime isn't installed.
            Log.Write($"on-device speech unavailable: {e}");
            throw new EngineNotSupportedException($"Windows AI speech isn't available here ({e.Message}).");
        }
        Log.Write($"on-device speech ready state: {state}");

        switch (state)
        {
            case AIFeatureReadyState.NotSupportedOnCurrentSystem:
                throw new EngineNotSupportedException("This PC can't run the on-device speech model.");
            case AIFeatureReadyState.DisabledByUser:
                throw new TranscriberException(
                    "The on-device speech model is turned off. Enable it under Settings > System > AI components.");
            case AIFeatureReadyState.Ready:
                break;
            default:
                // NotReady / EnsureNeeded: the optional model has to be downloaded through Windows Update.
                var agreed = await askUser(
                    "Murmur needs the optional speech recognition model, which Windows downloads in the background " +
                    "through Windows Update. You can watch progress in Settings > Windows Update and remove the model " +
                    "later under Settings > System > AI components.\n\nDownload it now?");
                if (!agreed) throw new TranscriberException("The speech recognition model wasn't downloaded.");
                status("Downloading the speech recognition model…");
                var result = await factory.EnsureReadyAsync();
                Log.Write($"EnsureReadyAsync: {result.Status} {result.ExtendedError?.Message}");
                if (result.Status != AIFeatureReadyResultState.Success)
                    throw new TranscriberException(
                        $"The speech recognition model couldn't be installed ({result.Status}: {result.ExtendedError?.Message}).");
                break;
        }

        status("Loading the speech recognition model…");
        _model = await factory.CreateAsync();
        Log.Write("on-device speech model created");
    }

    public async Task StartAsync()
    {
        var model = _model ?? throw new TranscriberException("The speech model isn't ready yet.");
        string deviceId;
        try
        {
            deviceId = MediaDevice.GetDefaultAudioCaptureId(AudioDeviceRole.Default);
        }
        catch (Exception e)
        {
            throw new TranscriberException($"No microphone found ({e.Message}).");
        }
        if (string.IsNullOrEmpty(deviceId)) throw new TranscriberException("No microphone found.");

        var recognition = new StreamingRecognition(AudioConfiguration.FromAudioDevice(deviceId), model);
        recognition.Recognizing += OnRecognizing;
        recognition.Recognized += OnRecognized;
        lock (_gate)
        {
            _phrases.Clear();
            _partial = "";
            _flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _recognition = recognition;
        }
        try
        {
            await recognition.StartContinuousRecognitionAsync();
        }
        catch (Exception e)
        {
            Release(recognition);
            throw new TranscriberException(DescribeStartFailure(e));
        }
    }

    public async Task<string> StopAsync()
    {
        StreamingRecognition? recognition;
        TaskCompletionSource? flushed;
        lock (_gate)
        {
            recognition = _recognition;
            flushed = _flushed;
            _recognition = null;
        }
        if (recognition is null) return "";
        try
        {
            await recognition.StopContinuousRecognitionAsync();
            OnCaptureStopped?.Invoke();
            // Give the engine a moment to turn the last partial result into a final one.
            if (flushed is not null) await Task.WhenAny(flushed.Task, Task.Delay(TimeSpan.FromMilliseconds(1500)));
        }
        catch (Exception e)
        {
            Log.Write($"StopContinuousRecognitionAsync: {e.Message}");
        }
        finally
        {
            Release(recognition);
            OnCaptureStopped?.Invoke();
        }
        lock (_gate)
        {
            var parts = _phrases.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            if (_partial.Trim().Length > 0) parts.Add(_partial.Trim()); // released mid-phrase: keep what was heard
            _phrases.Clear();
            _partial = "";
            return string.Join(" ", parts);
        }
    }

    public async Task CancelAsync()
    {
        StreamingRecognition? recognition;
        lock (_gate)
        {
            recognition = _recognition;
            _recognition = null;
            _phrases.Clear();
            _partial = "";
        }
        if (recognition is null) return;
        try
        {
            await recognition.StopContinuousRecognitionAsync();
        }
        catch (Exception e)
        {
            Log.Write($"cancel: {e.Message}");
        }
        finally
        {
            Release(recognition);
            OnCaptureStopped?.Invoke();
        }
    }

    private void OnRecognizing(StreamingRecognition sender, StreamingRecognizingEventArgs args)
    {
        Log.Write($"recognizing: \"{args.Text}\"");
        lock (_gate) _partial = args.Text ?? "";
        OnSound?.Invoke(true);
    }

    private void OnRecognized(StreamingRecognition sender, StreamingRecognizedEventArgs args)
    {
        Log.Write($"recognized final={args.IsFinal}: \"{args.Text}\"");
        TaskCompletionSource? flushed;
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(args.Text)) _phrases.Add(args.Text);
            _partial = "";
            flushed = _recognition is null ? _flushed : null; // after Stop, this is the flush we were waiting for
        }
        flushed?.TrySetResult();
        OnSound?.Invoke(false);
    }

    private void Release(StreamingRecognition recognition)
    {
        recognition.Recognizing -= OnRecognizing;
        recognition.Recognized -= OnRecognized;
        try
        {
            recognition.Dispose();
        }
        catch (Exception e)
        {
            Log.Write($"dispose recognition: {e.Message}");
        }
    }

    private static string DescribeStartFailure(Exception e)
    {
        if ((uint)e.HResult == 0x80070005 || e is UnauthorizedAccessException)
            return "Microphone access is blocked. Allow Murmur in Settings > Privacy & security > Microphone.";
        return $"Couldn't start listening: {e.Message}";
    }

    public void Dispose()
    {
        StreamingRecognition? recognition;
        lock (_gate)
        {
            recognition = _recognition;
            _recognition = null;
        }
        if (recognition is not null) Release(recognition);
        _model?.Dispose();
        _model = null;
    }
}
