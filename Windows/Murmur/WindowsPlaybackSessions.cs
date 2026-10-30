using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Murmur;

static class WindowsPlaybackSessions
{
    public static IEnumerable<IPlaybackSession> Enumerate()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in devices)
        {
            using (device)
            {
                var found = new List<IPlaybackSession>();
                try
                {
                    var sessions = device.AudioSessionManager.Sessions;
                    for (var i = 0; i < sessions.Count; i++)
                    {
                        AudioSessionControl? control = null;
                        try
                        {
                            control = sessions[i];
                            if (control.State != AudioSessionState.AudioSessionStateActive) continue;
                            found.Add(new Session(control, device.ID + ":" + control.GetSessionInstanceIdentifier));
                            control = null; // ownership transferred to the ducker
                        }
                        catch (Exception e) { Log.Write($"Playback session: {e.Message}"); }
                        finally { control?.Dispose(); }
                    }
                }
                catch (Exception e) { Log.Write($"Playback device: {e.Message}"); }
                foreach (var session in found) yield return session;
            }
        }
    }

    private sealed class Session(AudioSessionControl control, string id) : IPlaybackSession
    {
        public string Id => id;
        public int ProcessId => unchecked((int)control.GetProcessID);
        public float Volume
        {
            get => control.SimpleAudioVolume.Volume;
            set => control.SimpleAudioVolume.Volume = value;
        }
        public void Dispose() => control.Dispose();
    }
}
