using System.Text.RegularExpressions;

namespace ASTranscriber.Translation.LlamaServer;

public class ResponseCleaner
{
    private static readonly string[] _patterns =
    [
        "^(here'?s|sure|certainly|of course|note that|please note|clarification)",
        "(translation|translat(es|ed)|in english|in japanese)",
        "(input|output)",
        @"[\(\[].*?[\)\]]",
        "^i (would|will|have)",
    ];

    public static bool IsTextValid(string text)
    {
        var escaped = Regex.Escape(text);
        return _patterns.All(pattern => !Regex.IsMatch(escaped, pattern));
    }
}
