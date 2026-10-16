using System.Text.RegularExpressions;

namespace KingdomRush.ModPatcher.Core.Discovery;

public static partial class SteamVdfParser
{
    public static IReadOnlyList<string> ParseLibraryPaths(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Balanced(text, '{', '}')) return [];
        MatchCollection matches = QuotedToken().Matches(text);
        if (matches.Count == 0 || text.Count(character => character == '"') % 2 != 0) return [];
        List<string> paths = [];
        for (int index = 0; index + 1 < matches.Count; index++)
        {
            string key = Decode(matches[index].Groups[1].Value);
            if (!string.Equals(key, "path", StringComparison.OrdinalIgnoreCase)) continue;
            string value = Decode(matches[index + 1].Groups[1].Value);
            if (string.IsNullOrWhiteSpace(value)) continue;
            string full;
            try { full = Path.GetFullPath(value); }
            catch (Exception error) when (error is ArgumentException or NotSupportedException) { continue; }
            if (!paths.Contains(full, StringComparer.OrdinalIgnoreCase)) paths.Add(full);
            index++;
        }
        return paths;
    }

    private static bool Balanced(string text, char open, char close)
    {
        int depth = 0;
        foreach (char character in text)
        {
            if (character == open) depth++;
            if (character == close && --depth < 0) return false;
        }
        return depth == 0;
    }

    private static string Decode(string token) => token.Replace("\\\"", "\"").Replace("\\\\", "\\");

    [GeneratedRegex("\"((?:\\\\.|[^\"\\\\])*)\"")]
    private static partial Regex QuotedToken();
}
