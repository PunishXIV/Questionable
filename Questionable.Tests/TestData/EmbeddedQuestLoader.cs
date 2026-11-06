// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @alydevs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Questionable.Tests.TestData;

// QUESTIONABLE_CHANGED_QUESTS limits validation to PR-changed paths; otherwise every embedded quest is used.
public sealed class EmbeddedQuestLoader : IEnumerable<object[]>
{
    internal const string ChangedQuestsEnvVar = "QUESTIONABLE_CHANGED_QUESTS";

    private readonly List<object[]> _data;

    public EmbeddedQuestLoader()
    {
        Assembly assembly = typeof(EmbeddedQuestLoader).Assembly;

        IEnumerable<string> manifestNames = assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith("QuestPaths/", StringComparison.Ordinal) &&
                        x.EndsWith(".json", StringComparison.Ordinal));

        HashSet<string>? filter = ReadChangedQuestsFilter();
        if (filter is { Count: > 0 })
            manifestNames = manifestNames.Where(filter.Contains);

        _data = manifestNames
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => new object[] { new EmbeddedQuest(x) })
            .ToList();
    }

    public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static HashSet<string>? ReadChangedQuestsFilter()
    {
        string? raw = Environment.GetEnvironmentVariable(ChangedQuestsEnvVar);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Accept ';' or newline separators; normalize to forward slashes.
        return raw
            .Split([';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.Replace('\\', '/'))
            .ToHashSet(StringComparer.Ordinal);
    }
}
