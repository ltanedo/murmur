using System.Text.RegularExpressions;

namespace Murmur.Speech;

/// <summary>
/// Rule-based stand-in for the Mac app's on-device language-model clean-up: removes
/// fillers, immediate word repeats and common false starts. Deliberately conservative.
/// </summary>
public static class Cleanup
{
    private static readonly Regex Fillers = new(@"\b(um+|uh+|erm?|ah+|hmm+|like,|you know,)\b[,\s]*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Repeats = new(@"\b(\w+)(\s+\1\b)+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FalseStart = new(@"\b(\w{1,3})-\s+(?=\w)", RegexOptions.Compiled); // "I- I think" -> "I think"
    private static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    public static string Apply(string text)
    {
        string result = Fillers.Replace(text, string.Empty);
        result = Repeats.Replace(result, "$1");
        result = FalseStart.Replace(result, string.Empty);
        result = Spaces.Replace(result, " ").Trim();
        if (result.Length > 0 && char.IsLower(result[0])) result = char.ToUpperInvariant(result[0]) + result[1..];
        return result;
    }
}
