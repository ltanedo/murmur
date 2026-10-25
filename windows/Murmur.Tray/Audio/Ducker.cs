using NAudio.CoreAudioApi;

namespace Murmur.Audio;

/// <summary>
/// Lowers every other app's audio session while dictating and restores it after
/// (Core Audio session volumes; no loopback re-routing). Mirrors the Mac levels:
/// Off, Lower to 30 %, Lower to 10 %, Mute.
/// </summary>
public sealed class Ducker
{
    private readonly Dictionary<string, (AudioSessionControl session, float volume)> ducked = new();

    public void Duck(Settings.Ducking mode)
    {
        if (mode == Settings.Ducking.Off) return;
        float factor = mode switch { Settings.Ducking.Lower30 => .3f, Settings.Ducking.Lower10 => .1f, _ => 0f };
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;
            int self = Environment.ProcessId;
            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                if (session.GetProcessID == (uint)self || session.State != AudioSessionState.AudioSessionStateActive) continue;
                string key = session.GetSessionInstanceIdentifier;
                if (ducked.ContainsKey(key)) continue;
                float original = session.SimpleAudioVolume.Volume;
                ducked[key] = (session, original);
                session.SimpleAudioVolume.Volume = original * factor;
            }
        }
        catch (Exception e) { Log.Warn("Ducking failed: " + e.Message); }
    }

    public async Task RestoreAsync(int fadeMilliseconds = 250)
    {
        foreach (var (session, volume) in ducked.Values)
        {
            try
            {
                float start = session.SimpleAudioVolume.Volume;
                for (int step = 1; step <= 5; step++)
                {
                    session.SimpleAudioVolume.Volume = start + (volume - start) * step / 5f;
                    await Task.Delay(fadeMilliseconds / 5);
                }
                session.SimpleAudioVolume.Volume = volume;
            }
            catch { }
        }
        ducked.Clear();
    }
}
