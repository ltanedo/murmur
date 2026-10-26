namespace Murmur;

sealed class TranscriberException(string message) : Exception(message);

/// <summary>Thrown by an engine's PrepareAsync when this PC can't run it at all, so the app should try the next engine.</summary>
sealed class EngineNotSupportedException(string message) : Exception(message);

/// <summary>
/// One way of turning the microphone into text. Murmur prefers the on-device engine
/// (<see cref="OnDeviceSpeechEngine"/>) and falls back to Windows' older online dictation
/// (<see cref="OnlineSpeechEngine"/>) only when the PC can't run the on-device one.
/// </summary>
interface ISpeechEngine : IDisposable
{
    /// <summary>Short label for the tray menu, e.g. "On-device".</summary>
    string Label { get; }

    /// <summary>True when audio never leaves the machine.</summary>
    bool IsOnDevice { get; }

    /// <summary>Fires when the engine hears speech start or stop, for the on-screen indicator.</summary>
    Action<bool>? OnSound { get; set; }

    /// <summary>Loads models, downloads them if the user agrees, and leaves the engine ready to start instantly.</summary>
    Task PrepareAsync(Func<string, Task<bool>> askUser, Action<string> status);

    /// <summary>Starts listening. Returns once the microphone is open.</summary>
    Task StartAsync();

    /// <summary>Stops listening, waits for the engine to finish the audio it has, and returns the transcript.</summary>
    Task<string> StopAsync();

    /// <summary>Stops listening and throws the audio away.</summary>
    Task CancelAsync();
}
