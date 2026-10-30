namespace Murmur;

/// <summary>Retains completed phrases and revises only the audio since the last pause.</summary>
sealed class PreviewPhrases
{
    private const int Rate = 16000;
    private readonly Queue<int> _boundaries = new();
    private readonly List<string> _completed = [];
    private int _start, _end, _silence;
    private bool _heardSpeech, _commit;

    // Caller serializes these methods with the microphone sample buffer lock.
    public void Observe(int totalSamples, int bufferSamples, bool speaking)
    {
        if (speaking) { _heardSpeech = true; _silence = 0; }
        else if (_heardSpeech)
        {
            _silence += bufferSamples;
            if (_silence >= Rate / 2)
            {
                _boundaries.Enqueue(totalSamples);
                _heardSpeech = false;
                _silence = 0;
            }
        }
    }

    public float[] Snapshot(List<float> samples)
    {
        _end = _boundaries.TryPeek(out var boundary) ? boundary : samples.Count;
        _end = Math.Min(_end, _start + Rate * 30); // Bound inference for uninterrupted speech.
        _commit = (_boundaries.TryPeek(out boundary) && _end == boundary) || _end - _start == Rate * 30;
        if (_end - _start < (_commit ? Rate / 4 : Rate)) return [];
        return samples.GetRange(_start, _end - _start).ToArray();
    }

    public string Complete(string text)
    {
        if (!_commit) return string.Join(" ", _completed.Append(text).Where(s => s.Length > 0));
        if (text.Length > 0) _completed.Add(text);
        _start = _end;
        while (_boundaries.TryPeek(out var boundary) && boundary <= _start) _boundaries.Dequeue();
        return string.Join(" ", _completed);
    }
}
