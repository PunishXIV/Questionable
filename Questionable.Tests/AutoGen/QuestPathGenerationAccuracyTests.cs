// Authored with LLM assistance, changes must be reviewed and owned by a human.
// Initial version reviewed and owned by @Deckerz

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Questionable.AutoGen.Generation;
using Questionable.Model.Questing;
using Questionable.Tests.TestData;
using Xunit;
using Xunit.Abstractions;

namespace Questionable.Tests.AutoGen;

// Regression floors catch broad path-generation regressions that individual rule tests miss.
public sealed class QuestPathGenerationAccuracyTests(GameDataFixture fixture, ITestOutputHelper output)
    : IClassFixture<GameDataFixture>
{
    // Baseline: 97.2% over 4,095 hand-authored quests.
    private const double SequenceMatchFloor = 0.95;

    // Baseline: 88.5% over the same set.
    private const double QuestMatchFloor = 0.85;

    // Samples the corpus shape; set to 1 for the full sweep.
    private const int SampleStride = 20;

    [GameDataFact]
    public void GeneratedPathsAgreeWithHandAuthoredOnes()
    {
        Assert.True(fixture.Available, "Game data became unavailable after test discovery.");

        int sequencesCompared = 0, sequencesMatched = 0, questsCompared = 0, questsMatched = 0;
        List<string> mismatches = [];

        foreach (EmbeddedQuest embedded in SampledQuests())
        {
            if (!TryReadQuestId(embedded, out ushort questId))
                continue;

            QuestRoot hand = ReadHandAuthored(embedded);

            QuestPathResult? result = fixture.Factory!.GenerateById(questId, GameDataFixture.Author);
            questsCompared++;
            var comparison = CompareQuest(hand, result?.Root, embedded.ShortName, mismatches);
            sequencesCompared += comparison.Compared;
            sequencesMatched += comparison.Matched;
            if (comparison.QuestMatched)
                questsMatched++;
        }

        Assert.True(questsCompared > 0, "No quests were compared; the embedded QuestPaths resources are missing.");

        double sequenceRate = (double)sequencesMatched / Math.Max(sequencesCompared, 1);
        double questRate = (double)questsMatched / Math.Max(questsCompared, 1);

        output.WriteLine($"quests    : {questsMatched}/{questsCompared} ({questRate:P1})");
        output.WriteLine($"sequences : {sequencesMatched}/{sequencesCompared} ({sequenceRate:P1})");
        foreach (string mismatch in mismatches)
            output.WriteLine($"  {mismatch}");

        Assert.True(sequenceRate >= SequenceMatchFloor,
            $"Sequence agreement {sequenceRate:P1} fell below the {SequenceMatchFloor:P0} floor " +
            $"({sequencesMatched}/{sequencesCompared}).");
        Assert.True(questRate >= QuestMatchFloor,
            $"Whole-quest agreement {questRate:P1} fell below the {QuestMatchFloor:P0} floor " +
            $"({questsMatched}/{questsCompared}).");
    }

    private static IEnumerable<EmbeddedQuest> SampledQuests()
    {
        Assembly assembly = typeof(EmbeddedQuest).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(x => x.StartsWith("QuestPaths/", StringComparison.Ordinal) &&
                        x.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(x => x, StringComparer.Ordinal)
            .Where((_, index) => index % SampleStride == 0)
            .Select(x => new EmbeddedQuest(x));
    }

    private static bool TryReadQuestId(EmbeddedQuest embedded, out ushort questId)
    {
        questId = 0;
        int underscore = embedded.ShortName.IndexOf('_');
        return underscore > 0 &&
               ushort.TryParse(embedded.ShortName[..underscore], NumberStyles.None, CultureInfo.InvariantCulture,
                   out questId);
    }

    internal static (int Compared, int Matched, bool QuestMatched) CompareQuest(
        QuestRoot hand, QuestRoot? generated, string name, List<string> mismatches)
    {
        int compared = 0, matched = 0;
        bool questMatched = generated != null;
        if (generated == null && mismatches.Count < 20)
            mismatches.Add($"{name}: generation returned no path");

        foreach (QuestSequence handSequence in hand.QuestSequence)
        {
            // Compare the final targeted interaction; earlier targets can be navigation waypoints.
            uint? target = handSequence.Steps.LastOrDefault(x => x.DataId is > 0)?.DataId;
            if (target == null)
                continue;

            compared++;
            List<uint> generatedTargets = generated == null ? [] :
            [
                .. generated.QuestSequence
                    .Where(x => x.Sequence == handSequence.Sequence)
                    .SelectMany(x => x.Steps)
                    .Where(x => x.DataId is > 0)
                    .Select(x => x.DataId!.Value)
            ];

            if (generatedTargets.Contains(target.Value))
                matched++;
            else
            {
                questMatched = false;
                if (mismatches.Count < 20)
                    mismatches.Add($"{name} seq {handSequence.Sequence}: " +
                                   $"want {target.Value}, got [{string.Join(",", generatedTargets)}]");
            }
        }

        return (compared, matched, questMatched);
    }

    private static QuestRoot ReadHandAuthored(EmbeddedQuest embedded)
    {
        using System.IO.Stream stream = embedded.OpenStream();
        return JsonSerializer.Deserialize<QuestRoot>(stream)
               ?? throw new InvalidOperationException($"'{embedded.ManifestName}' deserialized to a null quest.");
    }
}
