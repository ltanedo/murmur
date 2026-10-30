using System.Media;
using System.Text;

namespace Murmur;

sealed class RecordingSounds : IRecordingSounds
{
    private readonly MemoryStream _startWave = new(CreateWave(660, 990));
    private readonly MemoryStream _stopWave = new(CreateWave(660, 440));
    private readonly SoundPlayer _start;
    private readonly SoundPlayer _stop;

    public RecordingSounds()
    {
        _start = new SoundPlayer(_startWave);
        _stop = new SoundPlayer(_stopWave);
        _start.Load();
        _stop.Load();
    }

    public void Started() => Play(_start);
    public void Stopped() => Play(_stop);
    private static void Play(SoundPlayer player)
    {
        try { player.Play(); }
        catch (Exception e) { Log.Write($"Recording cue: {e.Message}"); }
    }

    // Two short notes with soft edges. Generated locally, with no sound files or downloads.
    internal static byte[] CreateWave(double firstHz, double secondHz)
    {
        const int rate = 22050, noteSamples = 1323; // 60 ms per note
        const int dataBytes = noteSamples * 2 * sizeof(short);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate);
        writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(dataBytes);
        foreach (var hz in new[] { firstHz, secondHz })
            for (var i = 0; i < noteSamples; i++)
            {
                var edge = Math.Min(1.0, Math.Min(i, noteSamples - 1 - i) / 220.0);
                writer.Write((short)(Math.Sin(2 * Math.PI * hz * i / rate) * edge * 0.18 * short.MaxValue));
            }
        return stream.ToArray();
    }

    public void Dispose()
    {
        _start.Stop(); _stop.Stop();
        _start.Dispose(); _stop.Dispose();
        _startWave.Dispose(); _stopWave.Dispose();
    }
}
