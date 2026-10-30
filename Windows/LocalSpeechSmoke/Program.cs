using Murmur;
using Whisper.net;

// Does not download models. Only --capture briefly opens the microphone, discarding captured audio.
Console.WriteLine("Native runtime: " + WhisperFactory.GetRuntimeInfo());
foreach (var (input, expected) in new[]
{
    ("[BLANK AUDIO]", ""),
    (" [BLANK_AUDIO] ", ""),
    ("[blank audio].", ""),
    ("[BLANK_AUDIO] [BLANK AUDIO]", ""),
    ("Hello [BLANK_AUDIO] world.", "Hello world."),
    ("[BLANK AUDIO] Keep this sentence.", "Keep this sentence."),
    ("The blank audio needs editing.", "The blank audio needs editing."),
    ("Keep [this annotation] and punctuation!", "Keep [this annotation] and punctuation!"),
    ("", "")
})
    if (LocalSpeechEngine.RemoveBlankAudioMarkers(input) != expected)
        throw new Exception($"Blank-audio filter failed for: {input}");
Console.WriteLine("PASS: blank-audio annotations removed; genuine text preserved.");
var streamed = new StreamingTranscript();
streamed.Update("one two three");
if (streamed.Text != "one") throw new Exception("Expected first word immediately");
streamed.Advance();
if (streamed.Text != "one two") throw new Exception("Expected word-by-word reveal");
streamed.Update("one two four five");
if (streamed.Text != "one two") throw new Exception("Stable prefix was replayed");
streamed.Finish();
if (streamed.Text != "one two four five") throw new Exception("Correction failed");
streamed.Update("four five six");
if (streamed.Text != "four five") throw new Exception("Rolling-window overlap was replayed");
streamed.Advance();
streamed.Update("four five six");
if (streamed.Advance()) throw new Exception("Identical hypothesis replayed");
streamed.Update(string.Join(" ", Enumerable.Range(0, 100)));
for (var tick = 0; tick < 6; tick++) streamed.Advance();
if (streamed.Advance()) throw new Exception("Burst reveal exceeded six animation ticks");
streamed.Update("");
if (streamed.Text != "" || streamed.Advance()) throw new Exception("Blank hypothesis left stale words");
streamed.Update("cancel this"); streamed.Clear();
if (streamed.Text != "" || streamed.Advance()) throw new Exception("Clear left pending words");
Console.WriteLine("PASS: word reveal, revisions, rolling overlap, burst catch-up and clearing.");
var decodeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var decodeCancelled = false;
var published = 0;
var decodeCalls = 0;
var previewErrors = new List<Exception>();
var blockedPreview = new LiveTranscriptionPreview(() => [0.1f], async (_, token) =>
{
    Interlocked.Increment(ref decodeCalls);
    decodeEntered.TrySetResult();
    try { await Task.Delay(Timeout.Infinite, token); }
    catch (OperationCanceledException) { decodeCancelled = true; }
    return "stale text"; // Even a decoder returning on cancellation must not publish.
}, _ => Interlocked.Increment(ref published), previewErrors.Add, TimeSpan.FromMilliseconds(10));
await decodeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
await blockedPreview.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
if (!decodeCancelled || published != 0 || decodeCalls != 1 || previewErrors.Count != 0)
    throw new Exception("Preview cancellation leaked stale text or overlapping work");
var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var failedPreview = new LiveTranscriptionPreview(() => [0.1f], (_, _) => throw new IOException("test failure"),
    _ => throw new Exception("Unexpected publication"), _ => failed.TrySetResult(), TimeSpan.FromMilliseconds(10));
await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
await failedPreview.StopAsync();
Console.WriteLine("PASS: preview cancellation suppresses stale output, drains work, and isolates errors.");
using (var missing = new LocalSpeechEngine(""))
{
    await ExpectFailure(() => missing.PrepareAsync(_ => throw new Exception("Unexpected download prompt"), _ => { }),
        "bundled local speech model is missing");
    await ExpectFailure(() => missing.StartAsync(), "not loaded");
    await missing.CancelAsync();
}
var invalidFile = Path.GetTempFileName();
try
{
    await File.WriteAllTextAsync(invalidFile, "This is a blocked download page, not a model.");
    using var invalid = new LocalSpeechEngine(invalidFile);
    await ExpectFailure(() => invalid.PrepareAsync(_ => Task.FromResult(false), _ => { }), "not a whisper.cpp GGML model");
}
finally { File.Delete(invalidFile); }
Console.WriteLine("PASS: native runtime load, missing model, invalid model, unprepared capture, idle cancellation.");

if (args.Length >= 2)
{
    using var engine = new LocalSpeechEngine(args[0]);
    var timer = System.Diagnostics.Stopwatch.StartNew();
    await engine.PrepareAsync(_ => throw new Exception("Unexpected download"), Console.WriteLine);
    Console.WriteLine($"Model load: {timer.Elapsed.TotalSeconds:F2}s");
    if (args.Contains("--capture"))
    {
        var audioCallbacks = 0;
        engine.OnSound = _ => Interlocked.Increment(ref audioCallbacks);
        await engine.WarmCaptureAsync();
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var stoppedCallbacks = Volatile.Read(ref audioCallbacks);
            await Task.Delay(1000); // prove readiness survives an idle, stopped stream
            if (Volatile.Read(ref audioCallbacks) != stoppedCallbacks)
                throw new Exception("Microphone kept delivering audio while idle");
            timer.Restart();
            await engine.StartAsync();
            Console.WriteLine($"Mic start to first captured buffer ({attempt + 1}): {timer.Elapsed.TotalMilliseconds:F0} ms");
            await engine.CancelAsync();
        }
        Console.WriteLine("PASS: repeated real microphone readiness, cancellation and no idle capture; audio discarded.");
    }
    using var reader = new NAudio.Wave.WaveFileReader(args[1]);
    if (reader.WaveFormat.SampleRate != 16000 || reader.WaveFormat.Channels != 1 || reader.WaveFormat.BitsPerSample != 16)
        throw new Exception("Test requires 16 kHz mono PCM16 audio");
    var pcm = new byte[checked((int)reader.Length)];
    reader.ReadExactly(pcm);
    var samples = new float[pcm.Length / 2];
    for (var i = 0; i < samples.Length; i++) samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
    var previewsReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var previews = new List<string>();
    var snapshotNumber = 0;
    var previewTimer = System.Diagnostics.Stopwatch.StartNew();
    var realPreview = new LiveTranscriptionPreview(() =>
    {
        var length = Math.Min(samples.Length, Interlocked.Increment(ref snapshotNumber) * 32000);
        return samples[Math.Max(0, length - 128000)..length];
    }, engine.TranscribeAsync, text =>
    {
        previews.Add(text);
        Console.WriteLine($"Live preview at {previewTimer.Elapsed.TotalSeconds:F2}s: {text}");
        if (previews.Count >= 2) previewsReady.TrySetResult();
    }, e => previewsReady.TrySetException(e), TimeSpan.FromMilliseconds(750));
    try { await previewsReady.Task.WaitAsync(TimeSpan.FromSeconds(30)); }
    finally { await realPreview.StopAsync(); }
    if (previews.All(string.IsNullOrWhiteSpace)) throw new Exception("No real-model live text");
    Console.WriteLine("PASS: rolling real-model previews; final decode follows on the same processor.");
    var nativeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var nativePublished = false;
    var cancellingPreview = new LiveTranscriptionPreview(() => samples, (audio, token) =>
    {
        var task = engine.TranscribeAsync(audio, token);
        nativeStarted.TrySetResult();
        return task;
    }, _ => nativePublished = true, e => nativeStarted.TrySetException(e), TimeSpan.FromMilliseconds(10));
    await nativeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await cancellingPreview.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    if (nativePublished) throw new Exception("Cancelled native preview published text");
    Console.WriteLine("PASS: native preview cancellation drains before final processor reuse.");
    timer.Restart();
    var text = await engine.TranscribeAsync(samples);
    Console.WriteLine($"Transcription ({samples.Length / 16000.0:F1}s audio): {timer.Elapsed.TotalSeconds:F2}s");
    Console.WriteLine(text);
    if (!text.Contains("country", StringComparison.OrdinalIgnoreCase)) throw new Exception("Expected JFK sample transcription");
    if (await engine.TranscribeAsync(new float[16000]) != "") throw new Exception("Silence produced text");
    Console.WriteLine("PASS: model load, speech transcription, silence rejection.");
}

static async Task ExpectFailure(Func<Task> action, string expected)
{
    try { await action(); }
    catch (TranscriberException e) when (e.Message.Contains(expected)) { return; }
    throw new Exception("Expected actionable failure: " + expected);
}
