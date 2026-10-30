namespace Murmur;

/// <summary>One rolling-window decode at a time; never queues stale audio or commits text.</summary>
sealed class LiveTranscriptionPreview
{
    private readonly CancellationTokenSource _cancel = new();
    private readonly Task _loop;

    public LiveTranscriptionPreview(Func<float[]> snapshot,
        Func<float[], CancellationToken, Task<string>> transcribe, Action<string> publish,
        Action<Exception> reportError, TimeSpan? interval = null)
    {
        _loop = Task.Run(async () =>
        {
            try
            {
                while (true)
                {
                    await Task.Delay(interval ?? TimeSpan.FromMilliseconds(750), _cancel.Token);
                    var audio = snapshot();
                    if (audio.Length == 0) continue;
                    var text = await transcribe(audio, _cancel.Token);
                    _cancel.Token.ThrowIfCancellationRequested();
                    publish(text);
                }
            }
            catch (OperationCanceledException) when (_cancel.IsCancellationRequested) { }
            catch (Exception e) { reportError(e); } // Final dictation still works if preview fails.
        });
    }

    public async Task StopAsync()
    {
        _cancel.Cancel();
        await _loop.ConfigureAwait(false);
        _cancel.Dispose();
    }
}
