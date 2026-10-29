using System.Text.RegularExpressions;

namespace Murmur;

/// <summary>
/// Optional filler-word clean-up. The Mac app uses Apple's on-device language model for this;
/// Windows has no equivalent in the box, so this is a small set of rules instead. It only ever
/// removes text, never rewrites it, so it can't invent words.
/// </summary>
static partial class Cleanup
{
    public static string Polish(string text)
    {
        var result = Filler().Replace(text, " ");
        result = Stutter().Replace(result, "$1");
        result = DoubleSpace().Replace(result, " ");
        result = SpaceBeforePunctuation().Replace(result, "$1");
        result = result.Trim();
        if (result.Length > 0 && char.IsLower(result[0])) result = char.ToUpper(result[0]) + result[1..];
        return result;
    }

    // "um", "uh", "er", "erm", "ah", "hmm", "mm" as whole words, with any punctuation stuck to them.
    [GeneratedRegex(@"(?<![\w'])(?:u+m+|u+h+|e+r+m*|a+h+|h+m+|m+m+)[,.]?(?![\w'])", RegexOptions.IgnoreCase)]
    private static partial Regex Filler();

    // "the the", "I I" -> one copy. Short words only, so "had had" style grammar survives.
    [GeneratedRegex(@"\b(\w{1,4})(?:[,\s]+\1\b)+", RegexOptions.IgnoreCase)]
    private static partial Regex Stutter();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex DoubleSpace();

    [GeneratedRegex(@"\s+([,.!?;:])")]
    private static partial Regex SpaceBeforePunctuation();
}
