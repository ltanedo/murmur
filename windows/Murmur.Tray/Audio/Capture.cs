using NAudio.Wave;

namespace Murmur.Audio;

/// <summary>
/// Microphone level meter for the overlay. The speech API captures its own audio, so
/// this only measures; it opens on key-down and closes 500 ms after key-up to mirror
/// the Mac app's timing. TODO (Windows agent): confirm a second capture client does
/// not starve the recognizer on exclusive-mode devices; if it does, drop to polling
/// the default endpoint's peak meter (IAudioMeterInformation) instead.
/// </summary>
public sealed class Capture : IDisposable
{
    public event Action<float>? Level; // 0..1 peak per buffer
    private WaveInEvent? input;

    public void Start()
    {
        if (input != null) return;
        input = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50 };
        input.DataAvailable += (_, e) =>
        {
            float peak = 0f;
            for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
            {
                float sample = Math.Abs(BitConverter.ToInt16(e.Buffer, i) / 32768f);
                if (sample > peak) peak = sample;
            }
            Level?.Invoke(peak);
        };
        try { input.StartRecording(); } catch (Exception e) { Log.Warn("Level meter unavailable: " + e.Message); input = null; }
    }

    public async Task StopAsync(int tailMilliseconds = 500)
    {
        await Task.Delay(tailMilliseconds);
        input?.StopRecording(); input?.Dispose(); input = null;
    }

    public void Dispose() { input?.StopRecording(); input?.Dispose(); input = null; }
}
