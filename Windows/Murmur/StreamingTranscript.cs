namespace Murmur;

/// <summary>Reveals only recognized words, retaining shared text when hypotheses change.</summary>
sealed class StreamingTranscript
{
    private string[] _words = [];
    private int _visible;
    private int _step = 1;
    public string Text => string.Join(" ", _words.Take(_visible));

    public void Update(string text)
    {
        var next = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var common = 0;
        while (common < _visible && common < next.Length && _words[common] == next[common]) common++;
        if (common == 0)
        {
            // A rolling audio window may drop old words: retain its already-visible overlap.
            for (var count = Math.Min(_visible, next.Length); count > 0; count--)
                if (_words.AsSpan(_visible - count, count).SequenceEqual(next.AsSpan(0, count)))
                {
                    common = count;
                    break;
                }
        }
        _words = next;
        _visible = common;
        if (_visible == 0 && next.Length > 0) _visible = 1;
        _step = Math.Max(1, (next.Length - _visible + 5) / 6);
    }

    public bool Advance()
    {
        var remaining = _words.Length - _visible;
        if (remaining == 0) return false;
        // Usually one word per 80 ms tick; accelerate bursts instead of building a long backlog.
        _visible += Math.Min(remaining, _step);
        return true;
    }

    public void Finish() => _visible = _words.Length;
    public void Clear() { _words = []; _visible = 0; }
}
