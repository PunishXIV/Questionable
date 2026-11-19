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

/// <summary>
///     Measures generated paths against the hand-authored ones and fails if agreement drops below the floors
///     below. This is the guard the individual rule tests cannot give: a change that improves one quest while
///     quietly breaking a hundred others shows up here.
///     <para>
///         The floors sit a little under the measured rates so ordinary noise does not fail the build; they are
///         a regression alarm, not a target. Raise them when the generator genuinely improves.
///     </para>
/// </summary>
public sealed class QuestPathGenerationAccuracyTest(GameDataFixture fixture, ITestOutputHelper output)
    : IClassFixture<GameDataFixture>
{
    /// <summary>Measured 97.2% over all 4095 hand-authored quests at the time of writing.</summary>
    private const double SequenceMatchFloor = 0.95;

    /// <summary>Measured 88.5% over the same set.</summary>
    private const double QuestMatchFloor = 0.85;

    /// <summary>
    ///     Every Nth embedded quest, so the test covers the whole corpus shape without spending minutes on it.
    ///     The full sweep is the same code with this set to 1.
    /// </summary>
    private const int SampleStride = 20;

    [Fact]
    public void GeneratedPathsAgreeWithHandAuthoredOnes()
    {
        if (!fixture.Available)
        {
            output.WriteLine(
                $"Skipped: no FFXIV installation found (set {GameDataFixture.GamePathEnvVar} to run these).");
            return;
        }

        int sequencesCompared = 0, sequencesMatched = 0, questsCompared = 0, questsMatched = 0;
        List<string> mismatches = [];

        foreach (EmbeddedQuest embedded in SampledQuests())
        {
            if (!TryReadQuestId(embedded, out ushort questId))
                continue;

            QuestRoot? hand = ReadHandAuthored(embedded);
            if (hand == null)
                continue;

            QuestPathResult? result = fixture.Factory!.GenerateById(questId, GameDataFixture.Author);
            if (result == null)
                continue;

            QuestRoot generated = result.Root;
            questsCompared++;
            bool questMatched = true;

            foreach (QuestSequence handSequence in hand.QuestSequence)
            {
                // The last targeted step of a sequence is the interaction it exists for; earlier ones are
                // navigation waypoints a generator has no way to know about.
                uint? target = handSequence.Steps.LastOrDefault(x => x.DataId is > 0)?.DataId;
                if (target == null)
                    continue;

                sequencesCompared++;
                List<uint> generatedTargets =
                [
                    .. generated.QuestSequence
                        .Where(x => x.Sequence == handSequence.Sequence)
                        .SelectMany(x => x.Steps)
                        .Where(x => x.DataId is > 0)
                        .Select(x => x.DataId!.Value)
                ];

                if (generatedTargets.Contains(target.Value))
                {
                    sequencesMatched++;
                }
                else
                {
                    questMatched = false;
                    if (mismatches.Count < 20)
                    {
                        mismatches.Add(
                            $"{embedded.ShortName} seq {handSequence.Sequence}: " +
                            $"want {target.Value}, got [{string.Join(",", generatedTargets)}]");
                    }
                }
            }

            if (questMatched)
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

    private static QuestRoot? ReadHandAuthored(EmbeddedQuest embedded)
    {
        try
        {
            using System.IO.Stream stream = embedded.OpenStream();
            return JsonSerializer.Deserialize<QuestRoot>(stream);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
