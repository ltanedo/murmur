namespace Murmur;

interface IPlaybackSession : IDisposable
{
    string Id { get; }
    int ProcessId { get; }
    float Volume { get; set; }
}

/// <summary>Owns session handles until restoration. Called only on the UI thread.</summary>
sealed class AudioDucker(Func<IEnumerable<IPlaybackSession>> enumerate, int ownProcessId, Action<string> log) : IDisposable
{
    private sealed record Saved(IPlaybackSession Session, float Original, float Lowered);
    private readonly Dictionary<string, Saved> _saved = [];
    public bool IsDucking { get; private set; }

    public void Begin()
    {
        if (IsDucking) return;
        IsDucking = true;
        Refresh();
    }

    public void Refresh()
    {
        if (!IsDucking) return;
        try
        {
            foreach (var session in enumerate())
            {
                var retained = false;
                try
                {
                    var id = session.Id;
                    if (session.ProcessId == ownProcessId || _saved.ContainsKey(id)) continue;
                    var original = session.Volume;
                    var lowered = original * 0.5f;
                    _saved.Add(id, new Saved(session, original, lowered));
                    retained = true;
                    session.Volume = lowered;
                }
                catch (Exception e) { log($"Audio ducking: {e.Message}"); }
                finally { if (!retained) Release(session); }
            }
        }
        catch (Exception e) { log($"Enumerating playback: {e.Message}"); }
    }

    public void End()
    {
        IsDucking = false;
        foreach (var saved in _saved.Values)
        {
            try
            {
                // Preserve deliberate mixer changes made while recording.
                if (Math.Abs(saved.Session.Volume - saved.Lowered) < 0.0001f)
                    saved.Session.Volume = saved.Original;
            }
            catch (Exception e) { log($"Restoring playback: {e.Message}"); }
            finally { Release(saved.Session); }
        }
        _saved.Clear();
    }

    private void Release(IPlaybackSession session)
    {
        try { session.Dispose(); }
        catch (Exception e) { log($"Releasing playback session: {e.Message}"); }
    }

    public void Dispose() => End();
}
