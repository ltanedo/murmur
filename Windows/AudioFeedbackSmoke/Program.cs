using Murmur;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

var music = new FakeState("music", 0.8f);
var own = new FakeState("murmur", 1f, 42);
var muted = new FakeState("muted", 0f);
var states = new List<FakeState> { music, own, muted };
var errors = new List<string>();
using (var ducker = new AudioDucker(() => states.Select(s => new FakeSession(s)), 42, errors.Add))
{
    ducker.Begin(); Equal(music.Volume, 0.4f); Equal(own.Volume, 1); Equal(muted.Volume, 0);
    ducker.Begin(); ducker.Refresh(); Equal(music.Volume, 0.4f);
    var late = new FakeState("new-device-session", 0.6f); states.Add(late);
    ducker.Refresh(); Equal(late.Volume, 0.3f);
    music.Volume = 0.25f; // a manual mixer adjustment must win
    ducker.End(); Equal(music.Volume, 0.25f); Equal(late.Volume, 0.6f);
    ducker.End(); Equal(music.Volume, 0.25f);
}
if (states.Any(s => s.Handles != 0)) throw new Exception("Playback handle leak");
states = [new FakeState("healthy", 0.8f), new FakeState("disconnected", 0.5f) { Fail = true }];
using (var ducker = new AudioDucker(() => states.Select(s => new FakeSession(s)), 42, errors.Add))
{
    ducker.Begin(); Equal(states[0].Volume, 0.4f);
}
Equal(states[0].Volume, 0.8f);
if (errors.Count == 0 || states.Any(s => s.Handles != 0)) throw new Exception("Disconnected session not handled");

var cueMusic = new FakeState("feedback", 0.9f);
var cues = new FakeSounds();
using (var feedback = new RecordingFeedback(new AudioDucker(() => [new FakeSession(cueMusic)], 42, errors.Add), cues))
{
    feedback.Begin(); feedback.Started(); feedback.Started();
    Equal(cueMusic.Volume, 0.45f);
    feedback.StopRequested(); feedback.StopRequested();
    Equal(cueMusic.Volume, 0.45f); // still ducked for trailing capture
    feedback.CaptureEnded(); feedback.CaptureEnded(); Equal(cueMusic.Volume, 0.9f);
    if (cues.Starts != 1 || cues.Stops != 1) throw new Exception("Duplicate recording cues");
    feedback.Begin(); feedback.CaptureEnded(); // failed mic start: no cues
    if (cues.Starts != 1 || cues.Stops != 1) throw new Exception("Failed start played a cue");
    feedback.Begin(); feedback.StopRequested(); feedback.Started(); feedback.CaptureEnded();
    if (cues.Starts != 2 || cues.Stops != 2) throw new Exception("Release during startup was lost");
    feedback.Begin(); feedback.Started(); // quitting while recording must restore
}
Equal(cueMusic.Volume, 0.9f);
if (!cues.Disposed || cueMusic.Handles != 0) throw new Exception("Feedback disposal leaked resources");
var up = RecordingSounds.CreateWave(660, 990);
var down = RecordingSounds.CreateWave(660, 440);
if (up.SequenceEqual(down)) throw new Exception("Cues must differ");
using (var wave = new WaveFileReader(new MemoryStream(up)))
    if (Math.Abs(wave.TotalTime.TotalMilliseconds - 120) > 1) throw new Exception("Invalid cue WAV duration");
Console.WriteLine("PASS: half volume, no repeated attenuation, new sessions, self exclusion, muted audio, manual changes, disconnects, restoration, cue lifecycle and WAVs.");

if (args.Contains("--live"))
{
    // Exercise real Windows volume controls using only this test process's silent playback session.
    using var output = new WaveOutEvent();
    output.Init(new SignalGenerator(44100, 2) { Gain = 0 });
    output.Play();
    await Task.Delay(250);
    IEnumerable<IPlaybackSession> OwnSessions()
    {
        foreach (var session in WindowsPlaybackSessions.Enumerate())
            if (session.ProcessId == Environment.ProcessId) yield return session;
            else session.Dispose();
    }
    var sessions = OwnSessions().ToArray();
    if (sessions.Length == 0) throw new Exception("Silent playback session was not found");
    try
    {
        var before = sessions.Select(s => s.Volume).ToArray();
        using (var ducker = new AudioDucker(OwnSessions, -1, Console.WriteLine))
        {
            ducker.Begin();
            for (var i = 0; i < sessions.Length; i++) Equal(sessions[i].Volume, before[i] * 0.5f);
            ducker.End();
            for (var i = 0; i < sessions.Length; i++) Equal(sessions[i].Volume, before[i]);
        }
    }
    finally { foreach (var session in sessions) session.Dispose(); output.Stop(); }
    Console.WriteLine("PASS: real Windows playback session halved and restored (silent test only).");
}

static void Equal(float actual, float expected)
{
    if (Math.Abs(actual - expected) > 0.0001f) throw new Exception($"Expected volume {expected}, got {actual}");
}

sealed class FakeState(string id, float volume, int processId = 7)
{
    public string Id = id;
    public float Volume = volume;
    public int ProcessId = processId;
    public int Handles;
    public bool Fail;
}
sealed class FakeSession : IPlaybackSession
{
    private readonly FakeState _state;
    private bool _disposed;
    public FakeSession(FakeState state) { _state = state; state.Handles++; }
    public string Id => _state.Id;
    public int ProcessId => _state.ProcessId;
    public float Volume
    {
        get => _state.Fail ? throw new IOException("Disconnected") : _state.Volume;
        set { if (_state.Fail) throw new IOException("Disconnected"); _state.Volume = value; }
    }
    public void Dispose() { if (!_disposed) _state.Handles--; _disposed = true; }
}
sealed class FakeSounds : IRecordingSounds
{
    public int Starts, Stops;
    public bool Disposed;
    public void Started() => Starts++;
    public void Stopped() => Stops++;
    public void Dispose() => Disposed = true;
}
