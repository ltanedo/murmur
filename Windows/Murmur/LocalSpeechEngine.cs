using NAudio.Wave;
using NAudio.CoreAudioApi;
using Whisper.net;
using System.Text.RegularExpressions;

namespace Murmur;

/// <summary>English CPU dictation with a user-supplied whisper.cpp model. Audio stays in memory.</summary>
sealed class LocalSpeechEngine(string modelPath) : ISpeechEngine
{
    private const int SampleRate = 16000;
    private const int MaxSamples = SampleRate * 300;
    private readonly object _gate = new();
    private readonly List<float> _samples = [];
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;
    private WasapiCapture? _capture;
    private bool _capturing;
    private string? _microphoneId;
    private TaskCompletionSource? _stopped;
    private TaskCompletionSource? _firstAudio;
    private Exception? _captureError;
    private bool _tooLong;
    private readonly CancellationTokenSource _shutdown = new();
    private Task _work = Task.CompletedTask;
    private LiveTranscriptionPreview? _preview;
    private PreviewPhrases _previewPhrases = new();
    public Action<string>? OnPreview { get; set; }

    public string Label => "Local Whisper (English, offline)";
    public bool IsOnDevice => true;
    public Action<bool>? OnSound { get; set; }
    public Action? OnCaptureStopped { get; set; }

    public async Task PrepareAsync(Func<string, Task<bool>> askUser, Action<string> status)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            throw new TranscriberException("The bundled local speech model is missing. Reinstall Murmur to restore ggml-base.en-q5_1.bin.");
        using (var header = new BinaryReader(File.OpenRead(modelPath)))
        {
            if (header.BaseStream.Length < 1024 || header.ReadUInt32() != 0x67676d6c)
                throw new TranscriberException("This is not a whisper.cpp GGML model. Select a complete ggml-base.en-q5_1.bin file.");
        }
        status("Loading local Whisper model…");
        _work = Task.Run(() =>
        {
            _factory = WhisperFactory.FromPath(modelPath);
            _processor = _factory.CreateBuilder().WithLanguage("en")
                .WithThreads(Math.Clamp(Environment.ProcessorCount / 2, 1, 8))
                .WithNoContext().WithTemperature(0).WithGreedySamplingStrategy().Build();
        });
        await _work;
    }

    public async Task StartAsync()
    {
        if (_processor is null) throw new TranscriberException("The local model is not loaded.");
        if (_capturing) throw new TranscriberException("Microphone capture is already running.");
        lock (_gate)
        {
            _samples.Clear();
            _captureError = null;
            _tooLong = false;
            _previewPhrases = new PreviewPhrases();
        }
        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _firstAudio = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var devices = new MMDeviceEnumerator();
        using var microphone = devices.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        if (_microphoneId != microphone.ID) ReleaseCapture();
        if (_capture is null)
        {
            _capture = new WasapiCapture(microphone, false, 20)
            {
                WaveFormat = new WaveFormat(SampleRate, 16, 1)
            };
            _microphoneId = microphone.ID;
            _capture.DataAvailable += CaptureData;
            _capture.RecordingStopped += CaptureStopped;
        }
        var capture = _capture;
        _capturing = true;
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            capture.StartRecording();
            // Capture starts on a worker. Do not announce readiness before it delivers audio.
            await _firstAudio.Task.WaitAsync(TimeSpan.FromSeconds(5), _shutdown.Token);
            Log.Write($"Microphone ready: {timer.Elapsed.TotalMilliseconds:F0} ms");
            if (OnPreview is { } publish)
                _preview = new LiveTranscriptionPreview(PreviewSamples, TranscribePreviewAsync, publish,
                    e => Log.Write($"Live preview: {e.Message}"));
        }
        catch
        {
            ReleaseCapture();
            throw;
        }
    }

    // Pay driver initialization at app launch, then stop. No idle recording or pre-roll.
    internal async Task WarmCaptureAsync()
    {
        try { await StartAsync(); }
        finally { await CancelAsync(); }
    }

    private float[] PreviewSamples()
    {
        lock (_gate)
        {
            return _previewPhrases.Snapshot(_samples);
        }
    }

    private async Task<string> TranscribePreviewAsync(float[] samples, CancellationToken token)
    {
        var text = await TranscribeAsync(samples, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        lock (_gate) return _previewPhrases.Complete(text);
    }

    private async Task StopPreviewAsync()
    {
        var preview = _preview;
        _preview = null;
        if (preview is not null) await preview.StopAsync().ConfigureAwait(false);
    }

    private void CaptureData(object? sender, WaveInEventArgs e)
    {
        if (!ReferenceEquals(sender, _capture)) return;
        double energy = 0;
        lock (_gate)
        {
            for (var i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                if (_samples.Count >= MaxSamples) { _tooLong = true; break; }
                var sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8)) / 32768f;
                _samples.Add(sample);
                energy += sample * sample;
            }
        }
        if (e.BytesRecorded > 0) _firstAudio?.TrySetResult();
        var speaking = e.BytesRecorded > 0 && Math.Sqrt(energy / (e.BytesRecorded / 2)) > 0.005;
        lock (_gate) _previewPhrases.Observe(_samples.Count, e.BytesRecorded / 2, speaking);
        OnSound?.Invoke(speaking);
    }

    private void CaptureStopped(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _capture)) return;
        lock (_gate) _captureError = e.Exception;
        _firstAudio?.TrySetException(e.Exception ?? new IOException("Microphone stopped before audio arrived."));
        _stopped?.TrySetResult();
        OnCaptureStopped?.Invoke();
    }

    private async Task EndCaptureAsync()
    {
        if (_capture is null || !_capturing) return;
        try
        {
            _capture.StopRecording();
            if (_stopped is not null) await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch { ReleaseCapture(); throw; }
        finally { _capturing = false; }
        if (_captureError is not null) ReleaseCapture();
        OnSound?.Invoke(false);
    }

    public async Task<string> StopAsync()
    {
        try { await EndCaptureAsync(); }
        finally { await StopPreviewAsync(); }
        float[] samples;
        lock (_gate)
        {
            samples = _samples.ToArray();
            _samples.Clear();
            if (_captureError is not null) throw new TranscriberException($"Microphone capture failed: {_captureError.Message}");
            if (_tooLong) throw new TranscriberException("Recording exceeded five minutes. Please dictate in shorter sections.");
        }
        return await TranscribeAsync(samples);
    }

    internal Task<string> TranscribeAsync(float[] samples) => TranscribeAsync(samples, _shutdown.Token);

    internal async Task<string> TranscribeAsync(float[] samples, CancellationToken cancellationToken)
    {
        var processor = _processor ?? throw new TranscriberException("The local model is not loaded.");
        // Ignore very short or silent captures; Whisper can invent text from silence.
        if (samples.Length < SampleRate / 4 || samples.Max(Math.Abs) < 0.005f) return "";
        var work = Task.Run(async () =>
        {
            var segments = new List<string>();
            await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
                if (!string.IsNullOrWhiteSpace(segment.Text)) segments.Add(segment.Text.Trim());
            return RemoveBlankAudioMarkers(string.Join(" ", segments));
        });
        _work = work;
        return await work;
    }

    // Whisper can emit this annotation for room noise even when the amplitude gate passes.
    // Filter model output independently of the optional filler-word cleanup setting.
    internal static string RemoveBlankAudioMarkers(string text)
    {
        var cleaned = Regex.Replace(text, @"\[\s*BLANK[\s_]+AUDIO\s*\]", " ",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (cleaned == text) return text;
        if (cleaned.All(c => char.IsWhiteSpace(c) || char.IsPunctuation(c))) return "";
        return Regex.Replace(cleaned, @"\s+", " ").Trim();
    }

    public async Task CancelAsync()
    {
        try { await EndCaptureAsync(); }
        finally
        {
            await StopPreviewAsync();
            lock (_gate) _samples.Clear();
        }
    }

    private void ReleaseCapture()
    {
        var capture = _capture;
        _capture = null;
        _capturing = false;
        _microphoneId = null;
        if (capture is null) return;
        capture.DataAvailable -= CaptureData;
        capture.RecordingStopped -= CaptureStopped;
        capture.Dispose();
    }

    public void Dispose()
    {
        ReleaseCapture();
        _shutdown.Cancel();
        StopPreviewAsync().GetAwaiter().GetResult();
        // Do not free native model memory while a load or transcription is using it.
        try { _work.GetAwaiter().GetResult(); } catch { /* Reported by the caller. */ }
        _processor?.Dispose();
        _factory?.Dispose();
        _shutdown.Dispose();
        lock (_gate) _samples.Clear();
    }
}
