using System.Text;
using System.Text.RegularExpressions;

namespace Questionable.Utils;

// Adapted from WrathCombo's DebugFile.GetLastLogs.
internal static class DalamudLog
{
    private static readonly Regex Header = new(@"^(\d{4}-\d{2}-\d{2} )?\d{2}:\d{2}:\d{2}\.\d{3}", RegexOptions.Compiled);

    internal static List<string> Read(string path, IReadOnlyCollection<string> plugins)
    {
        // Dalamud keeps the file open for writing.
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream, Encoding.UTF8);
        return Filter(Lines(reader), plugins);
    }

    private static IEnumerable<string> Lines(StreamReader reader)
    {
        while (reader.ReadLine() is { } line)
            yield return line;
    }

    internal static List<string> Filter(IEnumerable<string> lines, IReadOnlyCollection<string> plugins)
    {
        List<string> entries = [];
        StringBuilder? current = null;
        foreach (string line in lines)
        {
            if (Header.IsMatch(line))
            {
                if (current != null)
                    entries.Add(current.ToString());
                // Dalamud's own load and unload errors for a plugin are tagged LocalPlugin; WahTools keeps those too.
                current = plugins.Any(x => line.Contains($"[{x}]", StringComparison.Ordinal) ||
                                           line.Contains("[LocalPlugin]", StringComparison.Ordinal) && line.Contains(x, StringComparison.Ordinal))
                    ? new(line)
                    : null;
            }
            else if (current != null && line.Length > 0)
                current.Append('\n').Append(line);
        }

        if (current != null)
            entries.Add(current.ToString());
        return entries;
    }
}
