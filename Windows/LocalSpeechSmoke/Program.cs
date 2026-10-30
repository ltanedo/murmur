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
