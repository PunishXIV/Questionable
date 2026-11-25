using System.Text;

namespace Questionable.Windows.Common;

// The prefix and format are shared with other players' copied lists and the priority IPC; keep them stable.
internal static class QuestClipboard
{
    private const string Prefix = "qst:priority:";
    private const string LegacyPrefix = "qst:v1:";
    private const char Separator = ';';

    public static string Encode(IEnumerable<ElementId> questIds) =>
        Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join(Separator, questIds.Select(x => x.ToString()))));

    public static List<ElementId> Decode(string text)
    {
        text = text.Trim();
        string? prefix = text.StartsWith(Prefix, StringComparison.InvariantCulture) ? Prefix
            : text.StartsWith(LegacyPrefix, StringComparison.InvariantCulture) ? LegacyPrefix
            : null;
        if (prefix == null)
            return [];

        try
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(text[prefix.Length..]));
            return [.. decoded.Split(Separator).Select(ElementId.FromString)];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static List<ElementId> Paste() => Decode(ImGui.GetClipboardText() ?? string.Empty);

    public static void Copy(IEnumerable<ElementId> questIds) => ImGui.SetClipboardText(Encode(questIds));
}
