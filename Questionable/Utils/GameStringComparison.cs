using System.Text.RegularExpressions;

namespace Questionable.Utils;

internal static partial class GameStringComparison
{
    // Excel text can still contain decoration macros that addon text readers have already stripped.
    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled, matchTimeoutMilliseconds: 1000)]
    private static partial Regex MacroLiteralRegex();

    public static bool AreEqual(string? left, string? right) =>
        left == null ? right == null : right != null && Normalize(left) == Normalize(right);

    // A macro like <string(gstr1)> is text the game fills in (the player's name), so it matches anything.
    public static bool Matches(string? left, string? right)
    {
        if (AreEqual(left, right))
            return true;
        if (left == null || right == null)
            return false;
        if (MacroLiteralRegex().IsMatch(right))
            (left, right) = (right, left);
        if (!MacroLiteralRegex().IsMatch(left))
            return false;

        string pattern = string.Join(".*", MacroLiteralRegex().Split(left).Select(x => Regex.Escape(Normalize(x))));
        return Regex.IsMatch(Normalize(right), $"^{pattern}$", RegexOptions.Singleline, TimeSpan.FromSeconds(1));
    }

    public static string Normalize(string value) =>
        MacroLiteralRegex().Replace(value, string.Empty)
            .ReplaceLineEndings()
            .Replace('\u2013', '-')
            .Trim();
}
