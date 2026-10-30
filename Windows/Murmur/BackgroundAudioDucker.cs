using System.Collections.Concurrent;

namespace Murmur;

/// <summary>Serializes Core Audio calls off the hotkey/UI thread, including restoration.</summary>
sealed class BackgroundAudioDucker : IAudioDucker
{
    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _thread;
    private readonly IAudioDucker _inner;
    private int _refreshPending;
    private bool _disposed;

    public BackgroundAudioDucker(IAudioDucker inner)
    {
        _inner = inner;
        _thread = new Thread(() =>
        {
            try
            {
                foreach (var action in _queue.GetConsumingEnumerable())
                    try { action(); }
                    catch (Exception e) { Log.Write($"Audio worker: {e.Message}"); }
            }
            finally { _inner.Dispose(); }
        }) { IsBackground = true, Name = "Murmur playback volume" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    // Entry points are called by the UI thread. COM handles stay on the worker.
    public void Begin() { if (!_disposed) _queue.Add(_inner.Begin); }
    public void End() { if (!_disposed) _queue.Add(_inner.End); }
    public void Refresh()
    {
        if (_disposed || Interlocked.Exchange(ref _refreshPending, 1) != 0) return;
        _queue.Add(() =>
        {
            try { _inner.Refresh(); }
            finally { Interlocked.Exchange(ref _refreshPending, 0); }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _queue.CompleteAdding();
        _thread.Join(); // Drain ordered begin/end calls and restore before quitting.
        _queue.Dispose();
    }
}
