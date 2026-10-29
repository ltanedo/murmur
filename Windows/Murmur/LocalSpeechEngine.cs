using NAudio.Wave;
using Whisper.net;

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
    private WaveInEvent? _capture;
    private TaskCompletionSource? _stopped;
    private Exception? _captureError;
    private bool _tooLong;
    private readonly CancellationTokenSource _shutdown = new();
    private Task _work = Task.CompletedTask;

    public string Label => "Local Whisper (English, offline)";
    public bool IsOnDevice => true;
    public Action<bool>? OnSound { get; set; }

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

    public Task StartAsync()
    {
        if (_processor is null) throw new TranscriberException("The local model is not loaded.");
        if (_capture is not null) throw new TranscriberException("Microphone capture is already running.");
        lock (_gate)
        {
            _samples.Clear();
            _captureError = null;
            _tooLong = false;
        }
        _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new WaveInEvent { WaveFormat = new WaveFormat(SampleRate, 16, 1), BufferMilliseconds = 50 };
        _capture = capture;
        capture.DataAvailable += CaptureData;
        capture.RecordingStopped += CaptureStopped;
        try { capture.StartRecording(); }
        catch
        {
            ReleaseCapture();
            throw;
        }
        return Task.CompletedTask;
    }

    private void CaptureData(object? sender, WaveInEventArgs e)
    {
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
        OnSound?.Invoke(e.BytesRecorded > 0 && Math.Sqrt(energy / (e.BytesRecorded / 2)) > 0.005);
    }

    private void CaptureStopped(object? sender, StoppedEventArgs e)
    {
        lock (_gate) _captureError = e.Exception;
        _stopped?.TrySetResult();
    }

    private async Task EndCaptureAsync()
    {
        if (_capture is null) return;
        try
        {
            _capture.StopRecording();
            if (_stopped is not null) await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { ReleaseCapture(); }
        OnSound?.Invoke(false);
    }

    public async Task<string> StopAsync()
    {
        await EndCaptureAsync();
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

    internal async Task<string> TranscribeAsync(float[] samples)
    {
        var processor = _processor ?? throw new TranscriberException("The local model is not loaded.");
        // Ignore very short or silent captures; Whisper can invent text from silence.
        if (samples.Length < SampleRate / 4 || samples.Max(Math.Abs) < 0.005f) return "";
        var work = Task.Run(async () =>
        {
            var segments = new List<string>();
            await foreach (var segment in processor.ProcessAsync(samples, _shutdown.Token))
                if (!string.IsNullOrWhiteSpace(segment.Text)) segments.Add(segment.Text.Trim());
            return string.Join(" ", segments);
        });
        _work = work;
        return await work;
    }

    public async Task CancelAsync()
    {
        try { await EndCaptureAsync(); }
        finally { lock (_gate) _samples.Clear(); }
    }

    private void ReleaseCapture()
    {
        var capture = _capture;
        _capture = null;
        if (capture is null) return;
        capture.DataAvailable -= CaptureData;
        capture.RecordingStopped -= CaptureStopped;
        capture.Dispose();
    }

    public void Dispose()
    {
        ReleaseCapture();
        _shutdown.Cancel();
        // Do not free native model memory while a load or transcription is using it.
        try { _work.GetAwaiter().GetResult(); } catch { /* Reported by the caller. */ }
        _processor?.Dispose();
        _factory?.Dispose();
        _shutdown.Dispose();
        lock (_gate) _samples.Clear();
    }
}
