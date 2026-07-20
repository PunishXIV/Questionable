// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @alydevs

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Questionable.Tests.TestData;

/// <summary>
/// xUnit theory data source that enumerates every quest JSON embedded into the test assembly.
/// <para>
/// If the <c>QUESTIONABLE_CHANGED_QUESTS</c> environment variable is set to a non-empty value,
/// the enumeration is filtered to only the quests whose repo-relative path appears in that list.
/// The variable is a semicolon- or newline-separated list of paths from the repository root
/// (forward slashes), e.g. <c>QuestPaths/2.x - A Realm Reborn/Side Quests/Thanalan/Ul'dah/396_No Lady Is an Island.json</c>.
/// </para>
/// <para>
/// When the variable is unset or empty (the local-development case) every embedded quest is
/// returned. CI populates it from <c>git diff</c> between the PR base and head so the
/// framework only re-validates files touched by the pull request.
/// </para>
/// </summary>
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
