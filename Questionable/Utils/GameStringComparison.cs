using System.Text.RegularExpressions;

namespace Questionable.Utils;

internal static partial class GameStringComparison
{
    // Excel text can retain decoration macros that addon text readers have already stripped.
    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MacroLiteralRegex();

    public static bool AreEqual(string? left, string? right) =>
        left == null ? right == null : right != null && Normalize(left) == Normalize(right);

    public static string Normalize(string value) =>
        MacroLiteralRegex().Replace(value, string.Empty)
            .ReplaceLineEndings()
            .Replace('\u2013', '-')
            .Trim();
}
