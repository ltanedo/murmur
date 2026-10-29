namespace Murmur;

interface IRecordingSounds : IDisposable
{
    void Started();
    void Stopped();
}

/// <summary>Exactly one cue per transition, with volume restoration independent of transcription.</summary>
sealed class RecordingFeedback(AudioDucker ducker, IRecordingSounds sounds) : IDisposable
{
    private bool _active;
    private bool _started;
    private bool _stopRequested;
    private bool _stopPlayed;

    public void Begin()
    {
        if (_active) return;
        _active = true;
        _started = _stopRequested = _stopPlayed = false;
        ducker.Begin();
    }

    public void Started()
    {
        if (!_active || _started) return;
        _started = true;
        sounds.Started();
        if (_stopRequested) StopRequested();
    }

    public void StopRequested()
    {
        if (!_active) return;
        _stopRequested = true;
        if (!_started || _stopPlayed) return;
        _stopPlayed = true;
        sounds.Stopped();
    }

    public void CaptureEnded()
    {
        StopRequested();
        _active = false;
        ducker.End();
    }

    public void Dispose()
    {
        _active = false;
        ducker.Dispose();
        sounds.Dispose();
    }
}
